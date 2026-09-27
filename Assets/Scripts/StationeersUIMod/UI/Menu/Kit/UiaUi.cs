using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Menu.Kit
{
    /// <summary>
    /// Low-level UGUI factory + layout helpers for the Control Center. Everything is built in
    /// code (no prefabs/sprite assets) so the menu is self-contained and hot-reload safe: solid
    /// <see cref="Image"/> rectangles for surfaces, TMP for text, Unity's own layout groups and
    /// ScrollRect for arrangement. Kept deliberately small — the interactive widgets live in
    /// <see cref="UiaControls"/> and read their colours from <see cref="UiaTheme"/>.
    /// </summary>
    public static class UiaUi
    {
        public static GameObject Go(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        public static RectTransform Rect(GameObject go) => (RectTransform)go.transform;

        /// <summary>Stretch a RectTransform to fill its parent with an optional uniform inset.</summary>
        public static RectTransform Fill(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Image(Transform parent, Color color, string name = "img")
        {
            var go = Go(name, parent);
            var img = go.AddComponent<Image>();
            img.color = color;
            // Kit-wide curved look (FlorpyDorp: "make all the ui elements curved and rounded"):
            // every surface takes the shared 9-sliced rounded-rect sprite with baked AA.
            // Hairline rules just soften slightly, which suits the frosted-grey restyle.
            UiaImages.Round(img);
            return img;
        }

        /// <summary>A flat surface panel that fills its parent. Full-bleed surfaces (the scrim)
        /// stay sharp-cornered — rounding a screen-filling quad reads as vignette holes.</summary>
        public static Image Panel(Transform parent, Color color, string name = "panel")
        {
            var img = Image(parent, color, name);
            img.sprite = null; img.type = UnityEngine.UI.Image.Type.Simple; // full-bleed: no rounding
            Fill((RectTransform)img.transform);
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left, bool wrap = false)
        {
            var go = Go("text", parent);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = UiaTheme.Font();
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.enableWordWrapping = wrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.text = text ?? "";
            return t;
        }

        /// <summary>A thin outline around a graphic (cheap — no extra geometry).</summary>
        public static Outline OutlineOf(Graphic g, Color color, float px = 1f)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = color;
            o.effectDistance = new Vector2(px, px);
            o.useGraphicAlpha = false;
            return o;
        }

        // ---- layout ----

        public static VerticalLayoutGroup VLayout(RectTransform rt, float spacing,
            int padL = 0, int padR = 0, int padT = 0, int padB = 0,
            TextAnchor align = TextAnchor.UpperLeft, bool expandW = true)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padL, padR, padT, padB);
            v.childAlignment = align;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = expandW;
            v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HLayout(RectTransform rt, float spacing,
            int padL = 0, int padR = 0, int padT = 0, int padB = 0,
            TextAnchor align = TextAnchor.MiddleLeft, bool expandW = false)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(padL, padR, padT, padB);
            h.childAlignment = align;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = expandW;
            h.childForceExpandHeight = false;
            return h;
        }

        /// <summary>Fixed sizes are MINIMUMS too (2026-09-26 visual fix wave): a preferred-only
        /// height lets a layout group under space pressure crush every fixed row toward 0 while
        /// its Overflow-mode text keeps painting over the neighbours — the squash class behind
        /// the broken F10 render. This kit never wants a sub-preferred row; anything that should
        /// shrink lives inside a ScrollView, whose content is CSF-driven and unaffected. The
        /// explicit <paramref name="minW"/> still wins over the derived minimum.</summary>
        public static LayoutElement Size(GameObject go, float h = -1f, float w = -1f,
            float flexW = -1f, float flexH = -1f, float minW = -1f)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            if (h >= 0f) { le.preferredHeight = h; le.minHeight = h; }
            if (w >= 0f) { le.preferredWidth = w; le.minWidth = w; }
            if (minW >= 0f) le.minWidth = minW;
            if (flexW >= 0f) le.flexibleWidth = flexW;
            if (flexH >= 0f) le.flexibleHeight = flexH;
            return le;
        }

        /// <summary>A single-line text input. As of Kit v2 this is a THIN WRAPPER over
        /// <see cref="UiaInputs.TextInput"/> — the one input implementation (intrinsic layout
        /// height so no host can collapse it to the D-009 hairline, themed caret/selection,
        /// glass border, ASCII discipline, Esc-cancels-without-closing-F10). Same signature and
        /// contract as before for every existing caller: <paramref name="onChanged"/> fires live
        /// per keystroke, and the returned TMP_InputField accepts <c>.text = ...</c> seeding.
        /// New code should call <see cref="UiaInputs.TextInput"/> directly for commit/draft/
        /// validation support.</summary>
        public static TMPro.TMP_InputField InputField(Transform parent, string placeholder,
            System.Action<string> onChanged)
        {
            var opt = new UiaInputs.TextInputOptions();
            opt.Placeholder = placeholder;
            opt.OnChanged = onChanged;
            var handle = UiaInputs.TextInput(parent, opt);
            return handle != null ? handle.Field : null;
        }

        /// <summary>A vertical scroll region. Returns the CONTENT RectTransform (already carrying a
        /// top-to-bottom VerticalLayoutGroup + a ContentSizeFitter) — add rows to it directly. Also
        /// attaches a thin auto-hiding <see cref="UiaScrollbar"/> inside the viewport's own mask, so
        /// every list in the kit gets a position indicator for free (no caller wiring needed — it
        /// self-ticks and hides itself when the content fits).</summary>
        public static RectTransform ScrollView(Transform parent, out ScrollRect scroll, float spacing = UiaTheme.Gap)
        {
            var viewportGo = Go("scroll", parent);
            var viewportImg = viewportGo.AddComponent<Image>();
            viewportImg.color = new Color(0, 0, 0, 0.001f); // near-invisible; needed as the mask target
            Fill((RectTransform)viewportGo.transform);
            viewportGo.AddComponent<RectMask2D>();

            scroll = viewportGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            scroll.viewport = (RectTransform)viewportGo.transform;

            var contentGo = Go("content", viewportGo.transform);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            // Right gutter: the scroll bar draws in an overlay lane at the viewport's right edge,
            // and full-width content put buttons/sliders UNDER it (FlorpyDorp 2026-08-10). Every
            // scroll view reserves the lane so controls can never clip beneath the bar.
            content.offsetMax = new Vector2(-14f, 0f);
            VLayout(content, spacing, 0, 0, 0, 0);
            var fit = contentGo.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;

            UiaScrollbar.Create(scroll, (RectTransform)viewportGo.transform, content);
            return content;
        }
    }
}
