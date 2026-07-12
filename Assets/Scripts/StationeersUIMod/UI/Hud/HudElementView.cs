using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// A HudPanel whose identity, geometry, style and tier visibility come from ONE
    /// document element instead of hand-coded config. The document is the source of
    /// truth; views are renderers — HudSystem rebuilds them when the active document
    /// is replaced and relayouts them when it changes (HudProfileStore.Version feeds
    /// the layout hash, so the existing relayout machinery carries the designer).
    /// </summary>
    internal abstract class HudElementView : HudPanel
    {
        public HudElementDef Def;

        public override string Id => Def != null ? Def.Type + "_" + Def.Id : "element";

        /// <summary>Power-death participation keeps the 0.5.0 semantics: an element that
        /// exists ONLY while suited dies in the CRT collapse; anything the bare body also
        /// shows (hands, equipment) survives it.</summary>
        public override bool SuitTier => Def != null
            && (Def.Tiers & HudTierMask.Suited) != 0
            && (Def.Tiers & HudTierMask.Bare) == 0;

        /// <summary>Document elements have no per-element ConfigEntry — presence in the
        /// document IS the toggle (the visibility loop treats null as "always on").</summary>
        public override ConfigEntry<bool> Toggle => null;

        public override bool VisibleAt(HudTier tier)
        {
            if (Def == null) return false;
            switch (tier)
            {
                case HudTier.Bare: return (Def.Tiers & HudTierMask.Bare) != 0;
                case HudTier.Suited: return (Def.Tiers & HudTierMask.Suited) != 0;
                case HudTier.Robot: return (Def.Tiers & HudTierMask.Robot) != 0;
                default: return false;
            }
        }

        /// <summary>Element centre in canvas coords: its anchor point plus the stored
        /// offset, scaled. Uses the live screen half-extents (HudWarp is refreshed by
        /// RelayoutAll before Layout runs).</summary>
        protected Vector2 CenterFor(float scale)
        {
            return Def.AnchorPoint(Screen.width * 0.5f, Screen.height * 0.5f)
                + new Vector2(Def.X, Def.Y) * scale;
        }

        protected Vector2 SizeFor(float scale) => new Vector2(Def.W, Def.H) * scale;

        /// <summary>Per-corner radius with the −1 = "global CornerRadius" convention.</summary>
        protected static float Radius(float perCorner)
            => perCorner >= 0f ? perCorner
             : (HudConfig.CornerRadius != null ? HudConfig.CornerRadius.Value : 10f);

        protected float BorderWidthFor()
            => Def.BorderWidth >= 0f ? Def.BorderWidth
             : (HudConfig.BorderWidth != null ? HudConfig.BorderWidth.Value : 1.4f);

        protected Color FillColor() => HudPalette.Resolve(Def.Fill, HudPalette.PanelFill.Value);
        protected Color BorderColor() => HudPalette.Resolve(Def.Border, HudPalette.PanelBorder.Value);
        protected Color TextColor() => HudPalette.Resolve(Def.TextColor, HudPalette.TextValue.Value);

        /// <summary>The universal editable surface; widget views append their own props.
        /// Rendered generically by the F9 designer (Phase 4) — same contract as the
        /// config popup's DrawConfigWidget, but for document fields.</summary>
        public virtual void DescribeProps(List<HudProp> into)
        {
            var d = Def;
            into.Add(HudProp.Anchor("Anchor", () => (int)d.Anchor, v => d.Anchor = (HudAnchor)v));
            into.Add(HudProp.F("X", () => d.X, v => d.X = v, -2000f, 2000f));
            into.Add(HudProp.F("Y", () => d.Y, v => d.Y = v, -2000f, 2000f));
            into.Add(HudProp.F("Width", () => d.W, v => d.W = Mathf.Max(2f, v), 2f, 2200f));
            into.Add(HudProp.F("Height", () => d.H, v => d.H = Mathf.Max(2f, v), 2f, 1300f));
            into.Add(HudProp.I("Z order", () => d.Z, v => d.Z = v, -100, 100));
            into.Add(HudProp.Tier("Tiers", () => (int)d.Tiers, v => d.Tiers = (HudTierMask)v));
            into.Add(HudProp.Color("Fill", () => d.Fill, v => d.Fill = v));
            into.Add(HudProp.Color("Border", () => d.Border, v => d.Border = v));
            into.Add(HudProp.Color("Text / accent", () => d.TextColor, v => d.TextColor = v));
            into.Add(HudProp.F("Border width (-1 = global)", () => d.BorderWidth, v => d.BorderWidth = v, -1f, 8f));
            into.Add(HudProp.F("Corner TL (-1 = global)", () => d.RTL, v => d.RTL = v, -1f, 64f));
            into.Add(HudProp.F("Corner TR (-1 = global)", () => d.RTR, v => d.RTR = v, -1f, 64f));
            into.Add(HudProp.F("Corner BR (-1 = global)", () => d.RBR, v => d.RBR = v, -1f, 64f));
            into.Add(HudProp.F("Corner BL (-1 = global)", () => d.RBL, v => d.RBL = v, -1f, 64f));
            into.Add(HudProp.F("Font scale", () => d.FontScale, v => d.FontScale = Mathf.Clamp(v, 0.4f, 3f), 0.4f, 3f));
        }

        /// <summary>Default editor target: the element's own rect. Views with richer
        /// internals may add more.</summary>
        public override void CollectEditTargets(List<HudEditTarget> into, float scale)
        {
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            into.Add(new HudEditTarget
            {
                Title = Def.Type.ToString(),
                Palette = System.Array.Empty<string>(),
                Values = System.Array.Empty<ConfigEntryBase>(),
                CanvasRect = new Rect(c.x - s.x * 0.5f, c.y - s.y * 0.5f, s.x, s.y),
            });
        }
    }
}
