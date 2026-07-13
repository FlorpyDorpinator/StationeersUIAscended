using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StationeersUIMod.UI.Hud.Widgets
{
    /// <summary>
    /// A body-damage doll assembled from the player's own PNG art instead of vanilla's orange
    /// silhouette (<see cref="DamageDollBorrowWidget"/>) or the procedural primitives
    /// (<see cref="BodyDollWidget"/>). Seven parts — head, chest, abdomen, two arms, two legs —
    /// each an <see cref="Image"/> whose sprite comes from <see cref="Core.HudIconStore"/> (drop
    /// <c>&lt;name&gt;.png</c> into config/StationeersUIMod/HudIcons). White art tints cleanly, so
    /// each part is coloured by ITS body region's damage: head reads head damage, chest reads
    /// chest damage, everything else the whole-body ratio (the only per-region signals the
    /// sampler exposes — see HudSampler.DamageHead/Chest/Body01). Injured-only by default, like
    /// vanilla: the whole figure hides while the player is unhurt.
    ///
    /// The figure is proportional to the element rect and re-centres in it; the anatomical
    /// anchors are sensible defaults exposed as F9 sliders (figure scale, arm/leg spread and
    /// height) plus per-part sprite-name overrides, so the doll can be tuned live without a
    /// rebuild. Sprites late-resolve (the override folder is scanned once, in-world), so the
    /// part images retry until their sprite lands.
    /// </summary>
    internal sealed class PngDollWidget : HudElementView
    {
        private const int RegionHead = 0, RegionChest = 1, RegionBody = 2;

        private sealed class Part
        {
            public Image Img;
            public RectTransform Rt;
            public int Region;
            public string Key;          // config param name holding the sprite key
            public string DefaultKey;   // built-in sprite key
            public bool Mirror;         // flip horizontally (one source PNG serves both sides)
            public float Aspect = 1f;   // native w/h, captured when the sprite loads
            // Offset of the VISIBLE (opaque) content's centre from the image centre, normalized
            // to [-0.5, 0.5]. Lets Place anchor by the pixels, not the padded image box, so two
            // hand-drawn arms with unequal transparent margins still land symmetric.
            public float OffX, OffY;
        }

        // Draw order: torso first so the arms/legs overlap it at the shoulders/hips.
        private readonly Part[] _parts = new Part[7];
        private PanelGraphic _box;

        protected override void BuildContent(RectTransform root)
        {
            _box = MakePanel(root, "Box");

            _parts[0] = MakePart(root, "Chest", RegionChest, "iChest", "doll_chest", false);
            _parts[1] = MakePart(root, "Tummy", RegionBody, "iTummy", "doll_tummy", false);
            _parts[2] = MakePart(root, "Head", RegionHead, "iHead", "doll_head", false);
            _parts[3] = MakePart(root, "ArmL", RegionBody, "iArmL", "doll_arm_l", false);
            _parts[4] = MakePart(root, "ArmR", RegionBody, "iArmR", "doll_arm_r", false);
            _parts[5] = MakePart(root, "LegL", RegionBody, "iLegL", "doll_leg", true);  // mirror of the R leg
            _parts[6] = MakePart(root, "LegR", RegionBody, "iLegR", "doll_leg", false);
        }

        private Part MakePart(RectTransform root, string name, int region, string key, string defKey, bool mirror)
        {
            var img = MakeIcon(root, name);
            img.enabled = false; // a sprite-less Image draws a white box — stay hidden until loaded
            return new Part { Img = img, Rt = img.rectTransform, Region = region, Key = key, DefaultKey = defKey, Mirror = mirror };
        }

        public override void Layout(float scale)
        {
            Root.anchoredPosition = Vector2.zero;
            // Geometry is re-derived every UpdatePanel from the live sprite aspects, so nothing
            // to bake here beyond the box; keep the box in step with a resize immediately.
            RelayoutBox(scale);
        }

        private void RelayoutBox(float scale)
        {
            var c = CenterFor(scale);
            var s = SizeFor(scale);
            ((RectTransform)_box.transform).anchoredPosition = c;
            _box.SetShape(s.x, s.y, Radius(Def.RTL), Radius(Def.RTR), Radius(Def.RBR), Radius(Def.RBL));
        }

        public override void UpdatePanel(HudSnapshot snap, float scale)
        {
            // Resolve any sprite that hasn't landed yet (folder scanned once, in-world).
            for (int i = 0; i < _parts.Length; i++)
            {
                var p = _parts[i];
                if (p.Img.sprite == null)
                {
                    string key = Def.GetS(p.Key, p.DefaultKey);
                    var sp = Core.HudIconStore.TryGet(key);
                    if (sp != null)
                    {
                        p.Img.sprite = sp;
                        p.Aspect = sp.rect.height > 0f ? sp.rect.width / sp.rect.height : 1f;
                        MeasureContentOffset(sp, out p.OffX, out p.OffY);
                    }
                }
            }

            LayoutFigure(scale);

            float dHead = snap != null ? Mathf.Clamp01(snap.DamageHead01) : 0f;
            float dChest = snap != null ? Mathf.Clamp01(snap.DamageChest01) : 0f;
            float dBody = snap != null ? Mathf.Clamp01(snap.DamageBody01) : 0f;
            float worst = Mathf.Max(dHead, Mathf.Max(dChest, dBody));

            // Injured-only (the user's choice, matching vanilla): the whole doll hides while the
            // player is unhurt, so an empty box never sits there.
            bool injuredOnly = Def.GetB("injuredOnly", true);
            bool show = !injuredOnly || worst > 0.02f;

            bool showBox = Def.GetB("box", true) && show;
            _box.enabled = showBox;
            if (showBox)
            {
                _box.color = FillColor();
                _box.BorderColor = BorderColor();
                _box.BorderWidth = BorderWidthFor();
                ApplyGlass(_box);
            }

            bool wholeBody = Def.GetB("wholeBody", false);
            // The damage ramp's three stops. Each is a per-element colour ref (palette name or
            // "#RRGGBBAA"); left blank it falls back to its old source — the element accent for
            // healthy, the global Warn/Critical palette for the two damage stops — so an untouched
            // doll looks exactly as before.
            Color neutral = HudPalette.Resolve(Def.GetS("cHealthy", ""), TextColor());
            Color warn = HudPalette.Resolve(Def.GetS("cWarn", ""), HudPalette.Warn.Value);
            Color crit = HudPalette.Resolve(Def.GetS("cCrit", ""), HudPalette.Critical.Value);

            for (int i = 0; i < _parts.Length; i++)
            {
                var p = _parts[i];
                bool has = p.Img.sprite != null;
                p.Img.enabled = has && show;
                if (!has || !show) continue;

                float r = wholeBody ? worst
                        : p.Region == RegionHead ? dHead
                        : p.Region == RegionChest ? dChest
                        : dBody;
                p.Img.color = ColorFor(r, neutral, warn, crit);
            }
        }

        /// <summary>Position + size every part from the element rect and the F9 tuning params.
        /// Anchors are fractions of the figure height; part sizes follow each sprite's native
        /// aspect so the art is never distorted.</summary>
        private void LayoutFigure(float scale)
        {
            RelayoutBox(scale);
            var c = CenterFor(scale);
            var s = SizeFor(scale);

            float figScale = Def.GetF("figScale", 0.98f);
            // Keep a person aspect: cap the height so the widest spread still fits the rect width.
            float fh = Mathf.Min(s.y * figScale, s.x * 1.95f);
            float yNudge = Def.GetF("figY", 0f) * scale;
            float top = c.y + fh * 0.5f + yNudge;
            float Y(float frac) => top - frac * fh;

            float armSpread = Def.GetF("armSpread", 1f);
            float armYoff = Def.GetF("armY", 0f);
            float legSpread = Def.GetF("legSpread", 0.06f);
            float legYoff = Def.GetF("legY", 0f);

            // Torso column (baked anatomical fractions; tuned against the shipped art).
            float chestW = Size(_parts[0], 0.155f * fh);
            Place(_parts[0], c.x, Y(0.24f), 0.155f * fh);
            Place(_parts[1], c.x, Y(0.375f), 0.12f * fh);
            Place(_parts[2], c.x, Y(0.085f), 0.15f * fh);

            float armX = (chestW * 0.5f - 2f * scale) * armSpread;
            Place(_parts[3], c.x - armX, Y(0.34f + armYoff), 0.35f * fh);
            Place(_parts[4], c.x + armX, Y(0.34f + armYoff), 0.35f * fh);

            float legX = legSpread * fh;
            Place(_parts[5], c.x - legX, Y(0.73f + legYoff), 0.40f * fh);
            Place(_parts[6], c.x + legX, Y(0.73f + legYoff), 0.40f * fh);
        }

        /// <summary>The width a part will occupy at the given target height (for anchoring
        /// neighbours), without moving it.</summary>
        private static float Size(Part p, float targetH) => Mathf.Max(1f, targetH) * p.Aspect;

        private static void Place(Part p, float x, float y, float targetH)
        {
            float h = Mathf.Max(1f, targetH);
            float w = h * p.Aspect;
            // Anchor by the VISIBLE content: shift the image box so the opaque centre lands at
            // (x, y). Mirroring flips the horizontal content offset, so a mirrored leg stays
            // aligned too.
            float ox = (p.Mirror ? p.OffX : -p.OffX) * w;
            float oy = -p.OffY * h;
            p.Rt.anchoredPosition = new Vector2(x + ox, y + oy);
            p.Rt.sizeDelta = new Vector2(w, h);
            p.Rt.localScale = new Vector3(p.Mirror ? -1f : 1f, 1f, 1f);
        }

        /// <summary>Centre of the opaque pixels within a sprite, as an offset from the image
        /// centre normalized to [-0.5, 0.5]. HudIconStore gives each PNG its own readable
        /// texture, so a one-time pixel scan is safe and cheap. Fully-padded or unreadable art
        /// falls back to (0,0) = image-box centring.</summary>
        private static void MeasureContentOffset(Sprite sp, out float offX, out float offY)
        {
            offX = 0f; offY = 0f;
            try
            {
                var tex = sp.texture;
                if (tex == null) return;
                var px = tex.GetPixels32();
                int W = tex.width, H = tex.height;
                if (W <= 0 || H <= 0 || px.Length < W * H) return;
                int minX = W, maxX = -1, minY = H, maxY = -1;
                for (int yy = 0; yy < H; yy++)
                {
                    int row = yy * W;
                    for (int xx = 0; xx < W; xx++)
                    {
                        if (px[row + xx].a > 64)
                        {
                            if (xx < minX) minX = xx;
                            if (xx > maxX) maxX = xx;
                            if (yy < minY) minY = yy;
                            if (yy > maxY) maxY = yy;
                        }
                    }
                }
                if (maxX < minX || maxY < minY) return; // no opaque pixels
                offX = (minX + maxX + 1) * 0.5f / W - 0.5f;
                offY = (minY + maxY + 1) * 0.5f / H - 0.5f;
            }
            catch { }
        }

        /// <summary>The colour ref the F9 picker should SHOW for a ramp stop: the stored override
        /// if one is set, else the stop's current default source (a palette name or the element's
        /// own accent ref). Returning the live default — rather than an empty string — makes the
        /// picker open on the real colour and keep tracking the palette until the user overrides it.
        /// The render path still reads the raw param (blank) and resolves it against the same
        /// fallback, so leaving a picker untouched changes nothing.</summary>
        private string DollColorRef(string key, string defaultRef)
        {
            string s = Def.GetS(key, "");
            return string.IsNullOrEmpty(s) ? defaultRef : s;
        }

        /// <summary>Neutral (the element's accent) at rest, brightening through warn to crit as
        /// a region's damage ratio climbs — the same ramp the procedural doll uses.</summary>
        private static Color ColorFor(float r, Color neutral, Color warn, Color crit)
        {
            if (r <= 0.02f) return neutral;
            if (r <= 0.5f) return Color.Lerp(neutral, warn, r / 0.5f);
            return Color.Lerp(warn, crit, (r - 0.5f) / 0.5f);
        }

        public override void DescribeProps(List<HudProp> into)
        {
            base.DescribeProps(into);
            var d = Def;
            into.Add(HudProp.Bool("Only when injured", () => d.GetB("injuredOnly", true), v => d.SetB("injuredOnly", v)));
            into.Add(HudProp.Bool("Whole-body tint", () => d.GetB("wholeBody", false), v => d.SetB("wholeBody", v)));
            into.Add(HudProp.Bool("Background box", () => d.GetB("box", true), v => d.SetB("box", v)));

            // The doll's damage-ramp tints. Each part fades from the healthy colour through warning
            // to critical as its region's damage climbs. Each picker OPENS on the colour that stop
            // currently uses (the element accent for healthy, the global Warn/Critical palette for
            // the two damage stops) and tracks that palette by name until you pick your own; the
            // box fill/border are the base "Fill"/"Border" pickers above.
            into.Add(HudProp.Header("Doll damage colours"));
            into.Add(HudProp.Color("Healthy tint",
                () => DollColorRef("cHealthy", string.IsNullOrEmpty(d.TextColor) ? "HudTextValue" : d.TextColor),
                v => d.Set("cHealthy", v)));
            into.Add(HudProp.Color("Warning tint", () => DollColorRef("cWarn", "HudWarn"), v => d.Set("cWarn", v)));
            into.Add(HudProp.Color("Critical tint", () => DollColorRef("cCrit", "HudCritical"), v => d.Set("cCrit", v)));
            into.Add(HudProp.F("Figure scale", () => d.GetF("figScale", 0.98f), v => d.SetF("figScale", Mathf.Clamp(v, 0.3f, 1.2f)), 0.3f, 1.2f));
            into.Add(HudProp.F("Figure Y nudge", () => d.GetF("figY", 0f), v => d.SetF("figY", v), -80f, 80f));
            into.Add(HudProp.F("Arm spread", () => d.GetF("armSpread", 1f), v => d.SetF("armSpread", Mathf.Clamp(v, 0f, 3f)), 0f, 3f));
            into.Add(HudProp.F("Arm Y (frac)", () => d.GetF("armY", 0f), v => d.SetF("armY", Mathf.Clamp(v, -0.3f, 0.3f)), -0.3f, 0.3f));
            into.Add(HudProp.F("Leg spread (frac)", () => d.GetF("legSpread", 0.06f), v => d.SetF("legSpread", Mathf.Clamp(v, 0f, 0.4f)), 0f, 0.4f));
            into.Add(HudProp.F("Leg Y (frac)", () => d.GetF("legY", 0f), v => d.SetF("legY", Mathf.Clamp(v, -0.3f, 0.3f)), -0.3f, 0.3f));
            into.Add(HudProp.Text("Head PNG", () => d.GetS("iHead", "doll_head"), v => d.Set("iHead", v)));
            into.Add(HudProp.Text("Chest PNG", () => d.GetS("iChest", "doll_chest"), v => d.Set("iChest", v)));
            into.Add(HudProp.Text("Abdomen PNG", () => d.GetS("iTummy", "doll_tummy"), v => d.Set("iTummy", v)));
            into.Add(HudProp.Text("Left arm PNG", () => d.GetS("iArmL", "doll_arm_l"), v => d.Set("iArmL", v)));
            into.Add(HudProp.Text("Right arm PNG", () => d.GetS("iArmR", "doll_arm_r"), v => d.Set("iArmR", v)));
            into.Add(HudProp.Text("Left leg PNG", () => d.GetS("iLegL", "doll_leg"), v => d.Set("iLegL", v)));
            into.Add(HudProp.Text("Right leg PNG", () => d.GetS("iLegR", "doll_leg"), v => d.Set("iLegR", v)));
        }
    }
}
