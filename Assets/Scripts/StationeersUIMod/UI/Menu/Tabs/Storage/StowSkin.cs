using System;
using System.Collections.Generic;
using StationeersUIMod.UI.Menu.Kit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Tabs.Storage
{
    /// <summary>
    /// The 1:1 CONTENT-ROUND skin for the SmartStow pages (measured spec, 2026-09-26, the
    /// 4-specialist workflow against FlorpyDorp's refined concept). Every colour is a
    /// THEME-DERIVED FORMULA (the StowBadge pattern): the comment on each token records the
    /// concept-sampled RGB it reproduces on the shipped "Stationeers Blue" theme, and the
    /// formula keeps other themes tinting the whole family instead of freezing raw constants.
    ///
    /// <para>Semantic palette rule from the colour report: CYAN = active data (selected
    /// layout card, ACTIVE chip, header text); ORANGE stays reserved for the tab system and
    /// the warned bag card. Kit-level tokens (UiaTheme.Accent retune, BG levels, tab/collar
    /// gold) are agent K's; this file only derives per-element colours FROM the tokens.</para>
    /// </summary>
    internal static class StowSkin
    {
        // ---------------- fills (the teal-black SURFACE LADDER) ----
        //
        // COMPARISON-ROUND RETUNE: the old Panel-anchored lerps all RESOLVED to ~(13,22,28) —
        // column == card == band, so cards never read as raised plates. The ladder is now
        // derived from the ACCENT HUE alone via HSV (the same F1 derivation K uses for the
        // kit surfaces), which makes the resolved values independent of K's parallel window/
        // panel anchor retints. Arithmetic check on the shipped accent (~hue 191):
        // ColumnFill (4,15,18) / CardFill (18,31,35) / BandFill (2,16,19) — card minus column
        // = (14,16,17), comfortably over the >=12-point raise the spec demands.

        /// <summary>An accent-HUE surface: S/V from the measured concept ladder.</summary>
        private static Color Surface(float s, float v)
        {
            float h, s0, v0;
            Color.RGBToHSV(UiaTheme.Accent, out h, out s0, out v0);
            Color c = Color.HSVToRGB(h, s, v);
            c.a = 1f;
            return c;
        }

        /// <summary>Column panel fill — resolves ~#040F12 (4,15,18).</summary>
        public static Color ColumnFill
        {
            get { return Surface(0.78f, 0.070f); }
        }

        /// <summary>Unselected card/row fill — resolves ~#121F23 (18,31,35): a LIFT of ~14
        /// points above its column, so cards sit on the panel as raised plates.</summary>
        public static Color CardFill
        {
            get { return Surface(0.48f, 0.137f); }
        }

        /// <summary>Selected (browsed) card fill — the concept's active-card level,
        /// ~(2,28,33): more saturated AND brighter than rest cards. Opaque always.</summary>
        public static Color CardFillSelected
        {
            get { return Surface(0.94f, 0.129f); }
        }

        /// <summary>Card hover fill — half a rung above rest.</summary>
        public static Color CardFillHover
        {
            get { return Surface(0.52f, 0.155f); }
        }

        /// <summary>Card edge — "only ~10 RGB points above fill, reads as an edge not a
        /// frame" (measured right; unchanged formula).</summary>
        public static Color CardEdge
        {
            get { return Color.Lerp(CardFill, UiaTheme.Accent, 0.12f); }
        }

        /// <summary>The recessed icon plate on BAG cards — resolves ~(2,10,12): teal-dark and
        /// subtly lighter than black (the old formula resolved pure black).</summary>
        public static Color DeepPlate
        {
            get { return Surface(0.85f, 0.047f); }
        }

        /// <summary>EDITING/SHARE cell interior — resolves ~(2,16,19): a step DARKER than the
        /// columns.</summary>
        public static Color BandFill
        {
            get { return Surface(0.88f, 0.075f); }
        }

        /// <summary>Dropdown/input field fill — resolves ~(8,26,32).</summary>
        public static Color FieldFill
        {
            get { return Surface(0.75f, 0.125f); }
        }

        /// <summary>Field border, all four sides — target ~(50,98,112).</summary>
        public static Color FieldEdge
        {
            get { return Color.Lerp(FieldFill, UiaTheme.Accent, 0.30f); }
        }

        // ---------------- buttons ----------------

        /// <summary>Teal action-button fill (Use/Capture/Edit...) — resolves ~(24,41,48),
        /// the ladder's top resting rung.</summary>
        public static Color ButtonFill
        {
            get { return Surface(0.50f, 0.188f); }
        }

        /// <summary>Its DIM border — target ~(60,107,122): "~55% of the brightness currently
        /// used", soft, never crisp bright cyan.</summary>
        public static Color ButtonEdge
        {
            get { return Color.Lerp(ButtonFill, UiaTheme.Accent, 0.30f); }
        }

        // ---------------- text ----------------

        /// <summary>Soft body white — target ~(225,228,229)/(243,244,242): never pure white.</summary>
        public static Color SoftText
        {
            get { return Color.Lerp(UiaTheme.Text, Color.black, 0.09f); }
        }

        /// <summary>Muted/secondary text — target ~(110,195,195..210): light CYAN, an accent
        /// tint, replacing the grey-blue TextMute on these pages ("the biggest text-colour
        /// gap" per the colour report).</summary>
        public static Color MutedCyan
        {
            get { return Color.Lerp(UiaTheme.TextMute, UiaTheme.Accent, 0.45f); }
        }

        /// <summary>Column header caps — target ~(0,240,251): BRIGHTER than the accent fill.</summary>
        public static Color HeaderText
        {
            get { return Color.Lerp(UiaTheme.Accent, Color.white, 0.18f); }
        }

        /// <summary>Dark ink on solid cyan (ACTIVE chip, primary buttons) — target ~(0,30,42).</summary>
        public static Color DarkInk
        {
            get { return new Color(0f, 0.12f, 0.16f, 1f); }
        }

        /// <summary>Bare kebab dots — target ~(37,228,242).</summary>
        public static Color KebabDots
        {
            get { return Color.Lerp(UiaTheme.Accent, Color.white, 0.12f); }
        }

        // ---------------- widgets ----------------

        /// <summary>The concept's teal action button: dark teal fill, DIM 1px border, soft
        /// white label — no glass skin, no bright rim (the Use-button recipe; the solid-cyan
        /// Primary style stays the kit's, one per panel header).</summary>
        public static UiaControls.UiaButton TealButton(Transform parent, string label,
            Action onClick, float w, float h)
        {
            var go = UiaUi.Go("tbtn", parent);
            UiaUi.Size(go, h, w, flexW: w < 0f ? 1f : 0f);
            var bg = go.AddComponent<Image>();
            UiaImages.Round(bg);
            UiaUi.OutlineOf(bg, ButtonEdge, 1f);
            var t = UiaUi.Text(go.transform, label, 14f, SoftText, TextAlignmentOptions.Center);
            UiaUi.Fill((RectTransform)t.transform);
            // No-ellipsis rule: a flex-width label ("By UI Ascended class - 12 rules" on a
            // narrow window) steps down toward 9pt, then wraps inside the button, instead of
            // painting past its edges. Short fixed labels never shrink.
            t.margin = new Vector4(4f, 0f, 4f, 0f);
            StowShared.Fit(t, 14f, 9f, true);
            var btn = go.AddComponent<UiaControls.UiaButton>()
                .Init(bg, ButtonFill, Color.Lerp(ButtonFill, UiaTheme.Accent, 0.12f),
                    CardFillSelected);
            btn.OnClick = onClick;
            return btn;
        }

        /// <summary>Strip a kit kebab button to the concept's BARE cyan dots: transparent
        /// faces (a faint white wash on hover keeps affordance), no glass border, dots tinted
        /// bright cyan. The invisible hit area keeps the kit size.</summary>
        public static UiaControls.UiaButton BareKebab(Transform parent,
            Func<List<UiaComposite.MenuItem>> items, float w = 26f, float h = 28f)
        {
            var btn = UiaComposite.MenuButton(parent, items, w, h);
            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                Color clear = new Color(0f, 0f, 0f, 0f);
                Color hover = new Color(1f, 1f, 1f, 0.06f);
                btn.Init(img, clear, hover, clear);
            }
            var glass = btn.GetComponent<UiaGlassSkin>();
            if (glass != null) UnityEngine.Object.Destroy(glass);
            var icon = btn.GetComponentInChildren<UiaIconGraphic>();
            if (icon != null) icon.color = KebabDots;
            return btn;
        }

        /// <summary>An (i) help button stripped to the bare drawn glyph — the same chrome-less
        /// treatment as <see cref="BareKebab"/> (comparison round: enclosed boxes around bare
        /// glyph controls left the skin).</summary>
        public static UiaControls.UiaButton BareInfo(Transform parent, string helpText,
            float size = 22f)
        {
            var btn = UiaComposite.InfoButton(parent, helpText);
            UiaUi.Size(btn.gameObject, size, size, flexW: 0f);
            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                Color clear = new Color(0f, 0f, 0f, 0f);
                Color hover = new Color(1f, 1f, 1f, 0.06f);
                btn.Init(img, clear, hover, clear);
            }
            var glass = btn.GetComponent<UiaGlassSkin>();
            if (glass != null) UnityEngine.Object.Destroy(glass);
            var icon = btn.GetComponentInChildren<UiaIconGraphic>();
            if (icon != null) icon.color = MutedCyan;
            return btn;
        }

        /// <summary>The browsed-card selection treatment (redo round, close-up 15): ONE
        /// PanelGraphic child drawing the crisp rounded accent border AND the mod's REAL
        /// outer glow — the HUD boxes' halo pipeline, a continuous mesh falloff — instead of
        /// stacked sprite Outlines, whose discrete rings read as chunky banded steps with
        /// misaligned corners. Low strength on purpose: subtle, nothing pixelly at 4x.</summary>
        public static void SelectionHalo(Transform card)
        {
            var go = UiaUi.Go("halo", card);
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;                       // rides the card rect, never the layout
            UiaUi.Fill((RectTransform)go.transform);
            var pg = go.AddComponent<UI.Hud.PanelGraphic>();
            pg.raycastTarget = false;
            go.AddComponent<SelectionHaloDriver>().Panel = pg;
            go.transform.SetAsFirstSibling();             // under the card's own content
        }

        /// <summary>Per-frame shape/style driver for <see cref="SelectionHalo"/> (the
        /// FolderCardDriver idiom: flex-width cards only know their rect after layout, and the
        /// accent follows live theme edits). Every PanelGraphic setter is dirty-guarded, so a
        /// settled frame costs pointer reads. Dies with the card GO — no statics.</summary>
        internal sealed class SelectionHaloDriver : MonoBehaviour
        {
            public UI.Hud.PanelGraphic Panel;
            private RectTransform _rt;

            // The tuning knobs (peak ~10% of the border alpha's glow energy, reach ~8px,
            // feathered to zero): raise Glow first if the halo needs more presence.
            private const float Corner = 5f;
            private const float BorderW = 1.4f;
            private const float GlowStrength = 0.35f;
            private const float GlowReach = 8f;
            private const float Diffuse = 0.6f;
            private const float ExtraDiffuse = 0.35f;

            private void LateUpdate()
            {
                if (Panel == null) return;
                if (_rt == null) _rt = transform as RectTransform;
                if (_rt == null) return;
                var size = _rt.rect.size;
                if (size.x < 2f || size.y < 2f) return;
                Color accent = UiaTheme.Accent;
                Panel.color = Color.clear;                // halo + border only, never a fill wash
                Panel.BorderColor = new Color(accent.r, accent.g, accent.b, 0.80f);
                Panel.BorderWidth = BorderW;
                Panel.Glow = GlowStrength;
                Panel.GlowWidth = GlowReach;
                Panel.GlowDiffuse = Diffuse;
                Panel.GlowExtraDiffuse = ExtraDiffuse;
                Panel.SetShape(size.x, size.y, Corner);
            }
        }

        /// <summary>The concept's under-shadow: every card sits ON the panel (offset-down
        /// soft black), replacing the old light bottom hairline that read as a bevel. Cheap —
        /// a UGUI Shadow duplicate, no blur shaders.</summary>
        public static void UnderShadow(Graphic g, float dist = 2f, float alpha = 0.30f)
        {
            if (g == null) return;
            var sh = g.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, alpha);
            sh.effectDistance = new Vector2(0f, -dist);
            sh.useGraphicAlpha = false;
        }
    }
}
