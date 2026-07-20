using System.Collections.Generic;
using BepInEx.Configuration;
using StationeersUIMod.UI.Hud;
using UnityEngine;

namespace StationeersUIMod.UI.Grid
{
    /// <summary>
    /// The Universal Inventory window's THEME SOURCE — the <see cref="Menu.Kit.UiaMenuTheme"/>
    /// pattern applied to the Grid. Two states:
    ///  • FOLLOW (default): the window is skinned from the GLOBAL box theme — the F9 palette's
    ///    <see cref="HudPalette.PanelFill"/> / <see cref="HudPalette.PanelBorder"/> /
    ///    <see cref="HudPalette.TextValue"/> plus <see cref="HudConfig"/>'s glass globals
    ///    (CornerRadius / GlassSheen / GlassEdge / BorderWidth) — so re-theming the visor
    ///    re-skins the Grid live, exactly like a follow-global Box.
    ///  • OVERRIDE: per-value config. Colours are ColorRef strings (a palette entry NAME or a
    ///    "#RRGGBBAA" literal, resolved by <see cref="HudPalette.Resolve"/>, so an override can
    ///    itself LINK to a palette entry). Floats use the HUD's -1 = "inherit the global"
    ///    convention, so a single knob can be overridden without pinning the rest.
    ///
    /// NOTE the Grid window is deliberately NOT a HUD document element (it is a modal,
    /// raycasting, un-warped, on-demand overlay on its own canvas), so its style cannot live in
    /// a profile's element list; and the palette/glass globals are SHARED config rather than
    /// per-profile. "Follows the profile" therefore holds only insofar as switching profiles
    /// changes those globals.
    ///
    /// Config only — no Unity objects, nothing to reset on F6 beyond the re-<see cref="Bind"/>.
    /// The getters are live, so the built window restyles whenever <see cref="StyleHash"/>
    /// changes (TheGridPanel polls it).
    /// </summary>
    public static class GridTheme
    {
        private const string Section = "13. The Grid";

        // ---- Surface variants -----------------------------------------------------------------
        // Every Grid box is one of these. A variant NEVER carries an absolute style value: it only
        // carries SCALES applied to the inherited theme (border width, corner radius) plus which
        // parts of the glass stack it opts into. A thin global border therefore stays thin here,
        // and a fat one stays fat, in every variant and in every hover state.

        /// <summary>Which Grid box is being painted. Drives only relative scales on the inherited
        /// theme — see <see cref="ApplyBox"/>.</summary>
        public enum GridSurface
        {
            /// <summary>The outer window shell (main window / a pinned window). The only surface
            /// that takes the glow halo and asks for the frosted backdrop, exactly as an outer HUD
            /// box does.</summary>
            Window,
            /// <summary>Small chrome buttons inside a window (close, shrink, sort) and the resize
            /// grip's plate: a tighter corner and a slightly finer line than the shell.</summary>
            Button,
            /// <summary>A top-level region box inside the list.</summary>
            Region,
            /// <summary>A nested (depth &gt; 0) region box — the same line, scaled down so nesting
            /// reads as recession rather than as a second, heavier frame.</summary>
            RegionNested,
            /// <summary>A tab in the tab strip.</summary>
            Tab,
            /// <summary>The tab drag ghost.</summary>
            TabGhost,
            /// <summary>One inventory CELL. The most numerous surface on screen, so it takes the
            /// finest line of all — and its four interaction states are scales on that line
            /// (<see cref="CellState"/>), never absolute widths.</summary>
            Cell
        }

        /// <summary>A cell's interaction state. Each maps to a SCALE on the inherited
        /// <see cref="BorderWidth"/> plus a palette colour — see <see cref="ApplyBox(PanelGraphic,
        /// float, float, CellState)"/>. A thin global border therefore stays thin in every state.</summary>
        public enum CellState
        {
            /// <summary>Nothing pointing at it: the inherited line, unscaled.</summary>
            Idle,
            /// <summary>Pointer over the cell.</summary>
            Hover,
            /// <summary>A move is in flight / this cell is the hovered drop target of a live
            /// drag — the transient "something is about to land here" cue.</summary>
            Pending,
            /// <summary>The local player's ACTIVE hand — a persistent, always-on identity cue, so
            /// it carries the heaviest line of the four.</summary>
            ActiveHand
        }

        public static ConfigEntry<bool> Follow;

        // Colour overrides (ColorRef strings). Used only when NOT following.
        public static ConfigEntry<string> FillRef;
        public static ConfigEntry<string> BorderRef;
        public static ConfigEntry<string> TextRef;

        // Float overrides. -1 = follow the global (works in either state, so a follower can
        // still be nudged only by unchecking Follow — kept uniform with the HUD's convention).
        public static ConfigEntry<float> CornerRadiusOv;
        public static ConfigEntry<float> SheenOv;
        public static ConfigEntry<float> SpecOv;

        public static void Bind(ConfigFile cfg)
        {
            Follow = cfg.Bind(Section, "FollowHudTheme", true,
                "Skin the Universal Inventory window from the global HUD box theme (palette " +
                "panel fill/border/text + the glass globals). Off = use the overrides below. " +
                "Edit visually in the F9 HUD editor: open the Grid while the editor is active " +
                "and click the window.");

            FillRef = cfg.Bind(Section, "GridFill", "HudPanelFill",
                "Window background when NOT following the HUD theme. A palette entry name " +
                "(e.g. HudPanelFill) or a #RRGGBBAA literal.");
            BorderRef = cfg.Bind(Section, "GridBorder", "HudPanelBorder",
                "Window outline when NOT following the HUD theme. A palette entry name or a " +
                "#RRGGBBAA literal.");
            TextRef = cfg.Bind(Section, "GridText", "HudTextValue",
                "Window text when NOT following the HUD theme. A palette entry name or a " +
                "#RRGGBBAA literal.");

            CornerRadiusOv = cfg.Bind(Section, "GridCornerRadius", -1f,
                new ConfigDescription("Window corner rounding (px). -1 = follow the global HUD " +
                    "CornerRadius.", new AcceptableValueRange<float>(-1f, 28f)));
            SheenOv = cfg.Bind(Section, "GridGlassSheen", -1f,
                new ConfigDescription("Glass sheen (the soft top-down light gradient) on the " +
                    "window. -1 = follow the global HUD GlassSheen.",
                    new AcceptableValueRange<float>(-1f, 1f)));
            SpecOv = cfg.Bind(Section, "GridGlassEdge", -1f,
                new ConfigDescription("Glass edge light (the bright rim highlight) on the " +
                    "window. -1 = follow the global HUD GlassEdge.",
                    new AcceptableValueRange<float>(-1f, 1f)));
        }

        public static bool Following { get { return Follow == null || Follow.Value; } }

        // ---- Hardcoded last-resort defaults (used before Bind and when a ref won't resolve) --

        private static readonly Color _defFill = new Color(0.05f, 0.07f, 0.09f, 0.88f);
        private static readonly Color _defBorder = new Color(0.35f, 0.78f, 0.90f, 0.65f);
        private static readonly Color _defText = new Color(0.94f, 0.97f, 1.00f, 1.00f);

        private static Color GlobalFill
        {
            get { return HudPalette.PanelFill != null ? HudPalette.PanelFill.Value : _defFill; }
        }

        private static Color GlobalBorder
        {
            get { return HudPalette.PanelBorder != null ? HudPalette.PanelBorder.Value : _defBorder; }
        }

        private static Color GlobalText
        {
            get { return HudPalette.TextValue != null ? HudPalette.TextValue.Value : _defText; }
        }

        // ---- Resolved getters (what TheGridPanel actually paints with) ----------------------

        public static Color Fill
        {
            get
            {
                if (Following || FillRef == null) return GlobalFill;
                return HudPalette.Resolve(FillRef.Value, GlobalFill);
            }
        }

        public static Color Border
        {
            get
            {
                if (Following || BorderRef == null) return GlobalBorder;
                return HudPalette.Resolve(BorderRef.Value, GlobalBorder);
            }
        }

        public static Color Text
        {
            get
            {
                if (Following || TextRef == null) return GlobalText;
                return HudPalette.Resolve(TextRef.Value, GlobalText);
            }
        }

        public static float CornerRadius
        {
            get { return Flt(CornerRadiusOv, HudConfig.CornerRadius, 10f); }
        }

        public static float Sheen
        {
            get { return Flt(SheenOv, HudConfig.GlassSheen, 0f); }
        }

        /// <summary>The rim/edge highlight — maps to <see cref="IGlassSurface.Spec"/>, whose
        /// global is <c>HudConfig.GlassEdge</c>.</summary>
        public static float Spec
        {
            get { return Flt(SpecOv, HudConfig.GlassEdge, 0f); }
        }

        /// <summary>Outline thickness. Always the global — the Grid exposes no override for it
        /// (one fewer knob; the border COLOUR is the expressive one).</summary>
        public static float BorderWidth
        {
            get { return HudConfig.BorderWidth != null ? HudConfig.BorderWidth.Value : 1.4f; }
        }

        private static float Flt(ConfigEntry<float> ov, ConfigEntry<float> global, float hard)
        {
            float g = global != null ? global.Value : hard;
            if (Following || ov == null || ov.Value < 0f) return g;
            return ov.Value;
        }

        /// <summary>The hover/active line is a DELTA on whatever the theme resolved, never an
        /// absolute: the inherited width times this. (A user on a 0.6px global border gets a 0.75px
        /// hover; on a 3px global border, 3.75px.)</summary>
        private const float HoverWidthScale = 1.25f;

        /// <summary>The interaction accent (hover / drag), palette-driven so it stays legible
        /// against any override. Falls back to the resolved border colour before the palette binds.</summary>
        private static Color Accent
        {
            get { return HudPalette.LineAccent != null ? HudPalette.LineAccent.Value : Border; }
        }

        /// <summary>Corner-radius scale per surface. Relative only — the radius itself is always
        /// <see cref="CornerRadius"/> (the Grid override, else the global HudConfig.CornerRadius).</summary>
        private static float RadiusScale(GridSurface s)
        {
            switch (s)
            {
                case GridSurface.Button: return 0.5f;
                case GridSurface.Region:
                case GridSurface.RegionNested: return 0.75f;
                case GridSurface.Tab:
                case GridSurface.TabGhost: return 0.5f;
                case GridSurface.Cell: return 0.5f;
                default: return 1f;   // Window
            }
        }

        /// <summary>How much of the box's SHORT side a corner may eat. A rect can round at most
        /// half its short side; a cell is small and square, so it keeps the softer 0.2 cap the
        /// cells were drawn with before the theme owned them (a half-round cell reads as a pill).</summary>
        private static float RadiusLimitFraction(GridSurface s)
        {
            return s == GridSurface.Cell ? 0.2f : 0.5f;
        }

        /// <summary>Border-width scale per CELL state. Relative only — see <see cref="CellState"/>.
        /// Ordering is deliberate: the active hand is the one persistent identity cue and so takes
        /// the heaviest line; hover/pending are transient and lean on COLOUR (the accent) rather
        /// than on weight.</summary>
        private static float CellWidthScale(CellState s)
        {
            switch (s)
            {
                case CellState.ActiveHand: return 1.6f;
                case CellState.Pending: return 1.35f;
                case CellState.Hover: return 1.25f;
                default: return 1f;   // Idle
            }
        }

        /// <summary>The palette colour a cell state paints its line with. Inherited theme border
        /// when idle, so a re-skin carries; accents otherwise.</summary>
        private static Color CellBorder(CellState s)
        {
            switch (s)
            {
                case CellState.ActiveHand:
                    return HudPalette.ActiveHandAccent != null
                        ? HudPalette.ActiveHandAccent.Value : Accent;
                case CellState.Pending:
                case CellState.Hover:
                    return Accent;
                default:
                    return Border;
            }
        }

        /// <summary>Which surfaces need <see cref="PanelGraphic.DenseFill"/>. A PanelGraphic's
        /// interior is a single triangle fan from one centre vertex, so ANY value that varies
        /// across the interior (the Sheen fill gradient, HudEdgeFade's alpha ramp) interpolates
        /// RADIALLY along the corner-to-centre triangles and paints a bowtie X — worst on a large,
        /// extreme-aspect, gradient-filled box (play-test: the Universal Inventory sized tall and
        /// narrow). Dense interior RINGS sample the ramp every ~48px so the ramp reproduces
        /// exactly. EVERY surface opts in: FlorpyDorp reported the X on ALL the inventory boxes "in
        /// differing amounts" in-game, which refutes the earlier assumption that small chrome's fan
        /// triangles are too short to show it — the error scales with size AND gradient strength, so
        /// a small box shows a small X, not none. This is cheap to make universal: PanelGraphic
        /// already forces the dense interior whenever Sheen is on
        /// (<c>_sheen &gt; 0.004f || _denseFill</c>), so on a sheened box this flag changes nothing,
        /// and it only adds rings where sheen is OFF and some other ramp (HudEdgeFade) would
        /// otherwise bowtie. The engine's own guard <c>minHalf &gt;= 8f &amp;&amp; columns &gt;= 3</c> already
        /// skips boxes too small to ring, so a cell (half ~23px) still qualifies while trivial chrome
        /// costs nothing. (The tab's <see cref="PolygonPanelGraphic"/> twin needs no equivalent: it
        /// EAR-CLIPS its interior rather than single-fanning it, so the bowtie mechanism does not
        /// exist there.)</summary>
        private static bool WantsDenseFill(GridSurface s)
        {
            return true;
        }

        /// <summary>Border-width scale per surface. Relative only — the width itself is always
        /// <see cref="BorderWidth"/> (the global HudConfig.BorderWidth).</summary>
        private static float WidthScale(GridSurface s)
        {
            switch (s)
            {
                case GridSurface.Button: return 0.8f;
                case GridSurface.Region: return 0.8f;
                case GridSurface.RegionNested: return 0.6f;   // 0.75x a top-level region
                case GridSurface.Tab:
                case GridSurface.TabGhost: return 0.8f;
                default: return 1f;   // Window
            }
        }

        /// <summary>
        /// THE one styling path for every box in UI/Grid — the window shells, their chrome buttons,
        /// region boxes and tabs. Resolves fill, border colour, border WIDTH, the corner shape
        /// (<see cref="PanelGraphic.SetShape(float,float,float,float,float)"/>), Sheen, Spec AND the
        /// full glass/frost stack <see cref="HudGlobalGlass.Apply"/> gives a HUD box — all from this
        /// theme (which follows the F9 palette + glass globals unless overridden). No caller may
        /// hardcode a width or a radius.
        ///
        /// <para><paramref name="hover"/> (hover / active / dragging) is applied as a DELTA on the
        /// inherited values — the accent border colour and <see cref="HoverWidthScale"/> times the
        /// inherited width — so a thin global border stays thin even when hovered.</para>
        ///
        /// <para>Per-frame safe and allocation-free: every <see cref="PanelGraphic"/> setter is
        /// dirty-guarded, <c>HudGlobalGlass.Apply</c>'s reads are null-guarded, and
        /// <c>HudFxMaterials.Assign</c> is idempotent. Owns no state, so nothing to reset on F6.</para>
        /// </summary>
        /// <param name="w">Laid-out width of the box, for the corner sweep.</param>
        /// <param name="h">Laid-out height of the box, for the corner sweep.</param>
        /// <param name="accent">Optional replacement for the hover accent (e.g. a tab dragged
        /// outside the strip). Ignored unless <paramref name="hover"/> is true.</param>
        public static void ApplyBox(PanelGraphic bg, float w, float h, GridSurface surface,
            bool hover, Color? accent = null)
        {
            if (bg == null) return;

            // --- colour + line, inherited then nudged -------------------------------------------
            float scale = WidthScale(surface);
            Color border = Border;
            if (hover)
            {
                scale *= HoverWidthScale;
                border = accent.HasValue ? accent.Value : Accent;
            }

            ApplyCore(bg, w, h, surface, scale, border);
        }

        /// <summary>The CELL entry point: same single styling path, with the four interaction
        /// states resolved as scales on the inherited line (<see cref="CellWidthScale"/>) and
        /// palette colours (<see cref="CellBorder"/>). Cells pass one square size; the corner is
        /// the inherited radius scaled and capped for a small box. No caller may hardcode a width
        /// or a radius.</summary>
        public static void ApplyBox(PanelGraphic bg, float w, float h, CellState state)
        {
            if (bg == null) return;
            ApplyCore(bg, w, h, GridSurface.Cell,
                WidthScale(GridSurface.Cell) * CellWidthScale(state), CellBorder(state));
        }

        private static void ApplyCore(PanelGraphic bg, float w, float h, GridSurface surface,
            float widthScale, Color border)
        {
            bool shell = surface == GridSurface.Window;

            float width = Mathf.Max(0f, BorderWidth) * widthScale;

            bg.color = Fill;
            bg.BorderColor = border;
            bg.BorderWidth = width;

            // --- shape: the inherited radius, scaled per surface and clamped to what this box can
            // actually draw (a half-height corner is the most a rect can round).
            float r = CornerRadius;
            if (float.IsNaN(r) || r < 0f) r = 0f;
            r *= RadiusScale(surface);
            float lim = Mathf.Min(Mathf.Abs(w), Mathf.Abs(h)) * RadiusLimitFraction(surface);
            if (r > lim) r = lim;
            bg.SetShape(w, h, r);

            // --- interior tessellation: kill the bowtie X on the big gradient surfaces ----------
            bg.DenseFill = WantsDenseFill(surface);

            // --- the HUD's own glass stack ------------------------------------------------------
            // The shell takes the glow halo and asks for the frosted backdrop (as an outer HUD box
            // does); interior surfaces follow sheen/edge-light/ripple but not the halo, so a window
            // full of regions doesn't bloom into a smear.
            // NOTE we deliberately do NOT write HudGlobalGlass.FrostDemand: it is a shared,
            // per-frame-owned flag (the Control Center assigns it every frame and clears it on
            // close), so a second writer would fight it. We consume the backdrop when it is live
            // and fall through to the edge-fx material when it is not.
            if (HudFxMaterials.Available)
            {
                HudGlobalGlass.Apply(bg, includeGlow: shell, wantFrost: shell, wantTierB: true);

                // Apply resolves Sheen/Spec from the GLOBALS; re-assert this theme's resolved values
                // so a Grid override still wins, preserving Apply's Tier-A edge-light boost as a
                // delta on top of the global edge light.
                float boost = bg.Spec - (HudConfig.GlassEdge != null ? HudConfig.GlassEdge.Value : 0f);
                bg.Sheen = Sheen;
                bg.Spec = Mathf.Clamp01(Spec + boost);
            }
            else
            {
                bg.Sheen = Sheen;
                bg.Spec = Spec;
                HudFxMaterials.Unassign(bg);
            }
        }

        /// <summary>A change signal for the built window: hashes the ACTUAL resolved output (so
        /// it fires for both a follow-mode palette drag and an override edit). Cheap cached
        /// palette reads; TheGridPanel polls it and restyles only on a real change.</summary>
        public static int StyleHash()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (Following ? 1 : 0);
                h = Comb(h, Fill);
                h = Comb(h, Border);
                h = Comb(h, Text);
                h = h * 31 + Mathf.RoundToInt(CornerRadius * 16f);
                h = h * 31 + Mathf.RoundToInt(Sheen * 512f);
                h = h * 31 + Mathf.RoundToInt(Spec * 512f);
                h = h * 31 + Mathf.RoundToInt(BorderWidth * 64f);
                return h;
            }
        }

        private static int Comb(int h, Color c)
        {
            unchecked
            {
                int q = (int)(Mathf.Clamp01(c.r) * 255f)
                    | ((int)(Mathf.Clamp01(c.g) * 255f) << 8)
                    | ((int)(Mathf.Clamp01(c.b) * 255f) << 16)
                    | ((int)(Mathf.Clamp01(c.a) * 255f) << 24);
                return h * 31 + q;
            }
        }

        /// <summary>Build the F9-style property list for the Grid's style popup (the same
        /// <see cref="HudProp"/> descriptors an element uses, so the drawer renders the identical
        /// palette dropdown + colour wheel a Box shows). Config-backed — no undo, BepInEx
        /// persists on write.</summary>
        public static void DescribeProps(List<HudProp> into)
        {
            into.Add(HudProp.Header("Universal Inventory style"));
            into.Add(HudProp.Bool("Follow the HUD's global box theme",
                () => Following, v => { if (Follow != null) Follow.Value = v; }));

            if (Following)
            {
                into.Add(HudProp.Header("Colours + glass follow F9 -> Palette + glass."));
                into.Add(HudProp.Header("Uncheck above to override each value."));
                return;
            }

            into.Add(HudProp.Header("Overrides (name a palette entry or type #RRGGBBAA)"));
            into.Add(HudProp.Color("Window fill",
                () => FillRef != null ? FillRef.Value : "",
                v => { if (FillRef != null) FillRef.Value = v ?? ""; },
                () => GlobalFill));
            into.Add(HudProp.Color("Window border",
                () => BorderRef != null ? BorderRef.Value : "",
                v => { if (BorderRef != null) BorderRef.Value = v ?? ""; },
                () => GlobalBorder));
            into.Add(HudProp.Color("Text",
                () => TextRef != null ? TextRef.Value : "",
                v => { if (TextRef != null) TextRef.Value = v ?? ""; },
                () => GlobalText));

            into.Add(HudProp.Header("Glass (-1 = follow the global)"));
            into.Add(HudProp.F("Corner radius",
                () => CornerRadiusOv != null ? CornerRadiusOv.Value : -1f,
                v => { if (CornerRadiusOv != null) CornerRadiusOv.Value = v; }, -1f, 28f));
            into.Add(HudProp.F("Glass sheen",
                () => SheenOv != null ? SheenOv.Value : -1f,
                v => { if (SheenOv != null) SheenOv.Value = v; }, -1f, 1f));
            into.Add(HudProp.F("Glass edge light",
                () => SpecOv != null ? SpecOv.Value : -1f,
                v => { if (SpecOv != null) SpecOv.Value = v; }, -1f, 1f));
        }
    }
}
