using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml.Serialization;
using StationeersUIMod.Core;
using UnityEngine;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>Where an element pins itself on screen. Offset (X,Y) is measured FROM this
    /// point, so a layout survives resolution changes without per-resolution maths.</summary>
    public enum HudAnchor
    {
        TopLeft, TopCenter, TopRight,
        MiddleLeft, Center, MiddleRight,
        BottomLeft, BottomCenter, BottomRight
    }

    /// <summary>Diegetic visibility tiers. An element only draws when the player's current
    /// state matches one of its flags — e.g. felt-sense words are Bare-only, threshold
    /// readouts are Suited-only. <see cref="HudTierMask.All"/> is the everyday default so a
    /// hand-authored element is never invisible by accident.</summary>
    [Flags]
    public enum HudTierMask { None = 0, Bare = 1, Suited = 2, Robot = 4, All = 7 }

    /// <summary>The concrete widget a <see cref="HudElementDef"/> instantiates. Primitives
    /// (Box/Label/Polyline/Icon) are author-drawn; the rest are bespoke views that bind to
    /// live game state. Order is append-only — the int is not persisted, but reordering would
    /// still churn diffs in hand-edited profiles, so new types go on the end.</summary>
    public enum HudElementType
    {
        Box, Label, Polyline, Icon,
        Readout, Clock, WorldName, DayCounter, ActiveHandBadge, Compass,
        MoodletDashboard, EquipmentColumn, HandBoxes, KeybindChips,
        Portrait, BodyDoll, SuitChips, BareSenses, Vignette,
        // Glassy 2.0 additions (append-only — see enum note above):
        VitalsPanel, DamageDoll, JetpackBox, StateChips,
        // A body doll assembled from user PNG art (config/StationeersUIMod/HudIcons), each
        // part tinted per-region by damage. Append-only.
        PngDoll
    }

    /// <summary>Which live value a <see cref="HudElementType.Readout"/> samples. Kept as a
    /// closed enum (not a free string) so the editor can offer a combo box and a bad profile
    /// can't ask for a source the sampler doesn't know how to read.</summary>
    public enum HudReadoutSource
    {
        ExternalPressure, ExternalTemp, ExternalO2,
        InternalPressure, InternalTemp,
        SuitTargetPressure, SuitTargetTemp,
        Nutrition, Hydration, Sanitation, Hygiene,
        JetpackThrust, JetpackPropellant, SuitPower,
        Health, O2Quality, Heading,
        /// <summary>The temperature you FEEL: breathing atmosphere when sealed, ambient
        /// otherwise — the vitals card's TEMP semantics (valid with ANY atmosphere,
        /// unlike InternalTemp which needs internals running).</summary>
        FeltTemp,
        /// <summary>Player movement speed in m/s (Human.VelocityMagnitude) — vanilla's
        /// external velocity readout.</summary>
        Speed,
    }

    /// <summary>
    /// The whole HUD as data: a flat, XML-serialised list of elements. There is no XML
    /// polymorphism here on purpose — every element is the same <see cref="HudElementDef"/>
    /// shape with a type discriminator, which keeps the serializer C# 7.3-simple and lets the
    /// editor treat elements uniformly.
    /// </summary>
    [XmlRoot("HudDocument")]
    public class HudDocument
    {
        /// <summary>Bumped when the on-disk shape changes incompatibly; lets a future load
        /// path migrate old profiles instead of silently mis-reading them.</summary>
        [XmlAttribute] public int Schema = 1;

        [XmlAttribute] public string Name;

        [XmlElement("El")] public List<HudElementDef> Elements = new List<HudElementDef>();

        /// <summary>Deep copy WITHOUT a serialize/deserialize round-trip. The editor's undo
        /// stack snapshots the whole document on every drag, so this runs many times a
        /// session — an XmlSerializer round-trip per snapshot would be needless GC and CPU.</summary>
        public HudDocument Clone()
        {
            var copy = new HudDocument { Schema = Schema, Name = Name };
            copy.Elements = new List<HudElementDef>(Elements.Count);
            for (int i = 0; i < Elements.Count; i++)
            {
                var el = Elements[i];
                if (el != null) copy.Elements.Add(el.Clone());
            }
            return copy;
        }

        /// <summary>Fail-soft repair after deserialize (rule 5): a hand-edited or drifted
        /// profile must never crash the HUD, only degrade the offending element. Repairs are
        /// counted and logged ONE line per category — a broken file with a hundred bad
        /// elements produces a handful of warnings, not a hundred.</summary>
        public void Sanitize()
        {
            if (Elements == null) { Elements = new List<HudElementDef>(); return; }

            int dropped = 0, mintedId = 0, clamped = 0, colorFixed = 0, tierFixed = 0, speedFixed = 0, bareOrphan = 0;
            var seenIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = Elements.Count - 1; i >= 0; i--)
            {
                var el = Elements[i];
                if (el == null) { Elements.RemoveAt(i); dropped++; continue; }

                // A missing OR duplicate Id is unusable: the editor keys selection and the
                // animator keys its seed off Id, so both must be present and unique.
                if (string.IsNullOrEmpty(el.Id) || seenIds.Contains(el.Id))
                {
                    el.Id = Guid.NewGuid().ToString("N");
                    mintedId++;
                }
                seenIds.Add(el.Id);

                if (el.W < 2f) { el.W = 2f; clamped++; }
                if (el.H < 2f) { el.H = 2f; clamped++; }

                if (string.IsNullOrEmpty(el.Fill)) { el.Fill = HudElementDef.DefaultFill; colorFixed++; }
                if (string.IsNullOrEmpty(el.Border)) { el.Border = HudElementDef.DefaultBorder; colorFixed++; }
                if (string.IsNullOrEmpty(el.TextColor)) { el.TextColor = HudElementDef.DefaultTextColor; colorFixed++; }

                if (el.Tiers == HudTierMask.None) { el.Tiers = HudTierMask.All; tierFixed++; }

                if (el.Params == null) el.Params = new List<HudParam>();

                // A bare-layout override only means something for a "Both" element (shown in bare
                // AND a live tier). If the element is single-mode (Bare-only or Live-only), the
                // override is dead data — strip it so it can't bloat the profile or desync a drag
                // (render vs edit tier). Idempotent, so it settles after one save.
                bool both = (el.Tiers & HudTierMask.Bare) != 0
                         && (el.Tiers & (HudTierMask.Suited | HudTierMask.Robot)) != 0;
                if (el.HasBareLayout && !both)
                {
                    el.SetBareLayout(false);
                    bareOrphan++;
                }

                // Legacy repair: a SPEED readout must vanish in the power-off (bare) HUD like
                // every other instrument. Early Add>Readout defaulted new elements to All-tier,
                // so hand-made speed boxes lingered in bare (play-test, repeatedly). Strip Bare
                // from any Speed-source readout; idempotent, so it settles after one save.
                if (el.Type == HudElementType.Readout
                    && string.Equals(el.GetS("src", ""), "Speed", StringComparison.OrdinalIgnoreCase)
                    && (el.Tiers & HudTierMask.Bare) != 0)
                {
                    el.Tiers &= ~HudTierMask.Bare;
                    if ((el.Tiers & (HudTierMask.Suited | HudTierMask.Robot)) == 0)
                        el.Tiers = HudTierMask.Suited | HudTierMask.Robot;
                    speedFixed++;
                }
            }

            string label = string.IsNullOrEmpty(Name) ? "HudDocument" : "HudDocument '" + Name + "'";
            if (dropped > 0) UIALog.Warn($"{label}: dropped {dropped} null element(s).");
            if (mintedId > 0) UIALog.Warn($"{label}: minted {mintedId} missing/duplicate element Id(s).");
            if (clamped > 0) UIALog.Warn($"{label}: clamped {clamped} sub-minimum element size(s) to 2px.");
            if (colorFixed > 0) UIALog.Warn($"{label}: defaulted {colorFixed} empty colour reference(s).");
            if (tierFixed > 0) UIALog.Warn($"{label}: reset {tierFixed} element(s) with no visible tier to All.");
            if (speedFixed > 0) UIALog.Warn($"{label}: made {speedFixed} Speed readout(s) suit-only (were showing in bare).");
            if (bareOrphan > 0) UIALog.Warn($"{label}: cleared {bareOrphan} orphaned bare-layout override(s) (element not shown in bare).");
        }

        /// <summary>Deterministic small hash of an Id, used to seed per-element animators
        /// (flicker phase, boot stagger) so two elements don't blink in lockstep. Deliberately
        /// NOT string.GetHashCode: that is randomised per process on some .NET runtimes, which
        /// would reshuffle every animation on each launch and — worse — break hot-reload, where
        /// the reloaded HUD must resume the same phases it left with.</summary>
        public static int StableSeed(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            // FNV-1a over the char bytes; folded into 0..9999 for a compact, stable seed.
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < id.Length; i++)
                {
                    char c = id[i];
                    hash = (hash ^ (byte)(c & 0xFF)) * 16777619u;
                    hash = (hash ^ (byte)(c >> 8)) * 16777619u;
                }
                return (int)(hash % 10000u);
            }
        }
    }

    /// <summary>
    /// One element, flat and XmlSerializer-friendly: every field is a scalar attribute except
    /// the <see cref="Params"/> bag. The type discriminator plus a shared style/geometry block
    /// lets a Box, a Readout and a Compass all round-trip through the same DTO.
    /// </summary>
    public class HudElementDef
    {
        /// <summary>The palette entry NAMES an element points at by default. Held as consts so
        /// the field initialisers and <see cref="HudDocument.Sanitize"/> agree on one source of
        /// truth (these match <c>HudPalette</c>'s bound names).</summary>
        public const string DefaultFill = "HudPanelFill";
        public const string DefaultBorder = "HudPanelBorder";
        public const string DefaultTextColor = "HudTextValue";

        [XmlAttribute] public string Id;
        [XmlAttribute] public HudElementType Type;

        [XmlAttribute] public HudAnchor Anchor = HudAnchor.Center;
        [XmlAttribute] public float X;
        [XmlAttribute] public float Y;
        [XmlAttribute] public float W = 120f;
        [XmlAttribute] public float H = 40f;
        /// <summary>Optional RELATIVE size: when > 0, width/height is this fraction of the
        /// SCREEN instead of the fixed W/H reference px — full-width bars survive ultrawide
        /// and 1440p without editing. -1 = fixed size.</summary>
        [XmlAttribute] public float WPct = -1f;
        [XmlAttribute] public float HPct = -1f;
        [XmlAttribute] public int Z;
        [XmlAttribute] public HudTierMask Tiers = HudTierMask.All;

        // Style. Colour fields are ColorRefs: a palette entry name OR a literal "#RRGGBBAA".
        [XmlAttribute] public string Fill = DefaultFill;
        [XmlAttribute] public string Border = DefaultBorder;
        [XmlAttribute] public string TextColor = DefaultTextColor;
        /// <summary>-1 defers to the HUD's global border width; a real value overrides it.</summary>
        [XmlAttribute] public float BorderWidth = -1f;

        // Per-corner radii (top-left, top-right, bottom-right, bottom-left); -1 = global CornerRadius.
        [XmlAttribute] public float RTL = -1f;
        [XmlAttribute] public float RTR = -1f;
        [XmlAttribute] public float RBR = -1f;
        [XmlAttribute] public float RBL = -1f;

        [XmlAttribute] public float FontScale = 1f;
        [XmlAttribute] public string Text;
        [XmlAttribute] public string Align = "Center";
        [XmlAttribute] public string Icon;

        /// <summary>Widget tunables that don't deserve a first-class field: a Readout's data
        /// source, a Polyline's points, per-widget flags. Kept a K/V bag so new widgets add
        /// knobs without touching the schema.</summary>
        [XmlElement("P")] public List<HudParam> Params = new List<HudParam>();

        private static readonly Vector2[] NoPoints = new Vector2[0];

        // --- param bag accessors ---
        // Index loops (not foreach) throughout: these are read on the draw path and must not
        // allocate an enumerator per element per frame.

        private HudParam Find(string key)
        {
            if (Params == null || key == null) return null;
            for (int i = 0; i < Params.Count; i++)
                if (Params[i] != null && Params[i].K == key) return Params[i];
            return null;
        }

        public float GetF(string key, float def)
        {
            var p = Find(key);
            float v;
            if (p != null && float.TryParse(p.V, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        public bool GetB(string key, bool def)
        {
            var p = Find(key);
            bool v;
            if (p != null && bool.TryParse(p.V, out v)) return v;
            return def;
        }

        public int GetI(string key, int def)
        {
            var p = Find(key);
            int v;
            if (p != null && int.TryParse(p.V, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        public string GetS(string key, string def)
        {
            var p = Find(key);
            return p != null && p.V != null ? p.V : def;
        }

        /// <summary>Set, replacing an existing key or appending a new one. A null value REMOVES
        /// the key — so a widget that reverts a tunable to its default drops the entry rather
        /// than persisting a redundant one.</summary>
        public void Set(string key, string v)
        {
            if (key == null) return;
            if (Params == null) Params = new List<HudParam>();
            for (int i = 0; i < Params.Count; i++)
            {
                if (Params[i] != null && Params[i].K == key)
                {
                    if (v == null) Params.RemoveAt(i);
                    else Params[i].V = v;
                    return;
                }
            }
            if (v != null) Params.Add(new HudParam { K = key, V = v });
        }

        // Invariant-culture formatting so a profile authored under a comma-decimal locale still
        // parses everywhere. "R" guarantees the float round-trips exactly on net48.
        public void SetF(string key, float v) { Set(key, v.ToString("R", CultureInfo.InvariantCulture)); }
        public void SetB(string key, bool v) { Set(key, v ? "true" : "false"); }
        public void SetI(string key, int v) { Set(key, v.ToString(CultureInfo.InvariantCulture)); }

        /// <summary>Decode a "x,y;x,y;..." point list (invariant culture). Returns the shared
        /// empty array — never null, never a fresh allocation — when the key is absent or any
        /// token is malformed, so a bad polyline draws nothing instead of throwing.</summary>
        public Vector2[] GetPoints(string key)
        {
            var p = Find(key);
            if (p == null || string.IsNullOrEmpty(p.V)) return NoPoints;

            string[] pairs = p.V.Split(';');
            var result = new Vector2[pairs.Length];
            int n = 0;
            for (int i = 0; i < pairs.Length; i++)
            {
                if (pairs[i].Length == 0) continue; // tolerate a trailing/empty ';'
                int comma = pairs[i].IndexOf(',');
                if (comma < 0) return NoPoints;
                float x, y;
                if (!float.TryParse(pairs[i].Substring(0, comma), NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return NoPoints;
                if (!float.TryParse(pairs[i].Substring(comma + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return NoPoints;
                result[n++] = new Vector2(x, y);
            }
            if (n == 0) return NoPoints;
            if (n == result.Length) return result;
            var trimmed = new Vector2[n];
            Array.Copy(result, trimmed, n);
            return trimmed;
        }

        public void SetPoints(string key, IList<Vector2> points)
        {
            if (points == null || points.Count == 0) { Set(key, null); return; }
            var sb = new StringBuilder(points.Count * 12);
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(points[i].x.ToString("R", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(points[i].y.ToString("R", CultureInfo.InvariantCulture));
            }
            Set(key, sb.ToString());
        }

        /// <summary>Manual field-by-field copy for the undo stack (see
        /// <see cref="HudDocument.Clone"/>). Deep-copies the param bag so an undo snapshot can't
        /// be mutated by later edits to the live element.</summary>
        public HudElementDef Clone()
        {
            var c = new HudElementDef
            {
                Id = Id,
                Type = Type,
                Anchor = Anchor,
                X = X, Y = Y, W = W, H = H, Z = Z,
                WPct = WPct, HPct = HPct,
                Tiers = Tiers,
                Fill = Fill,
                Border = Border,
                TextColor = TextColor,
                BorderWidth = BorderWidth,
                RTL = RTL, RTR = RTR, RBR = RBR, RBL = RBL,
                FontScale = FontScale,
                Text = Text,
                Align = Align,
                Icon = Icon,
            };
            if (Params != null)
            {
                c.Params = new List<HudParam>(Params.Count);
                for (int i = 0; i < Params.Count; i++)
                {
                    var p = Params[i];
                    if (p != null) c.Params.Add(new HudParam { K = p.K, V = p.V });
                }
            }
            return c;
        }

        // --- per-tier (bare) layout override -----------------------------------------
        // An element shown in BOTH bare and suit can carry a SECOND geometry for the power-off
        // (bare) HUD, so ONE element occupies a different spot/size per mode instead of being
        // duplicated. Duplication is not just clutter: two copies of a borrow widget
        // (Moodlet/DamageDoll/Portrait) fight over the one vanilla object they reparent. Stored
        // in the param bag — absent means "bare inherits the base layout", so most elements
        // carry nothing extra and profiles authored before this feature load unchanged.
        public bool HasBareLayout => GetB("bLayout", false);

        /// <summary>Geometry resolved for the tier being laid out. When <paramref name="bare"/>
        /// and an override exists, the bare values win; otherwise the base fields (which bare
        /// inherits).</summary>
        public HudAnchor AnchorFor(bool bare) => bare && HasBareLayout ? (HudAnchor)GetI("bAnchor", (int)Anchor) : Anchor;
        public float XFor(bool bare)    => bare && HasBareLayout ? GetF("bX", X) : X;
        public float YFor(bool bare)    => bare && HasBareLayout ? GetF("bY", Y) : Y;
        public float WFor(bool bare)    => bare && HasBareLayout ? Mathf.Max(2f, GetF("bW", W)) : W;
        public float HFor(bool bare)    => bare && HasBareLayout ? Mathf.Max(2f, GetF("bH", H)) : H;
        public float WPctFor(bool bare) => bare && HasBareLayout ? GetF("bWPct", WPct) : WPct;
        public float HPctFor(bool bare) => bare && HasBareLayout ? GetF("bHPct", HPct) : HPct;

        /// <summary>Turn a bare override on — seeded from the current base geometry, so it starts
        /// exactly where the suit layout sits and the designer nudges from there — or off, so bare
        /// reverts to inheriting the base layout. Idempotent.</summary>
        public void SetBareLayout(bool on)
        {
            if (on)
            {
                if (HasBareLayout) return;
                SetB("bLayout", true);
                SetI("bAnchor", (int)Anchor);
                SetF("bX", X); SetF("bY", Y);
                SetF("bW", W); SetF("bH", H);
                SetF("bWPct", WPct); SetF("bHPct", HPct);
            }
            else
            {
                Set("bLayout", null); Set("bAnchor", null);
                Set("bX", null); Set("bY", null);
                Set("bW", null); Set("bH", null);
                Set("bWPct", null); Set("bHPct", null);
            }
        }

        // Write helpers: when <paramref name="bare"/> they target the bare override (auto-enabling
        // it, so a drag with the preview set to BARE simply starts remembering a bare position);
        // otherwise they write the base fields.
        public void SetAnchorFor(bool bare, HudAnchor a) { if (bare) { SetBareLayout(true); SetI("bAnchor", (int)a); } else Anchor = a; }
        public void SetXFor(bool bare, float v) { if (bare) { SetBareLayout(true); SetF("bX", v); } else X = v; }
        public void SetYFor(bool bare, float v) { if (bare) { SetBareLayout(true); SetF("bY", v); } else Y = v; }
        public void SetWFor(bool bare, float v) { if (bare) { SetBareLayout(true); SetF("bW", v); } else W = v; }
        public void SetHFor(bool bare, float v) { if (bare) { SetBareLayout(true); SetF("bH", v); } else H = v; }
        public void SetWPctFor(bool bare, float v) { if (bare) { SetBareLayout(true); SetF("bWPct", v); } else WPct = v; }
        public void SetHPctFor(bool bare, float v) { if (bare) { SetBareLayout(true); SetF("bHPct", v); } else HPct = v; }

        // ---- per-curvature-mode LIVE layout (A/B/C/D each remember their own placement) ----
        // Same mechanism as the bare override, but keyed by the curvature mode instead of the
        // tier, and it only touches the LIVE (non-bare) layout — bare stays one shared override
        // (FlorpyDorp's choice). Stored in the param bag under a per-mode prefix, so profiles
        // authored before this feature (no such params) load unchanged and Flat uses the base.
        internal static string ModeKey(HudCurvature mode)
        {
            switch (mode)
            {
                case HudCurvature.VertexWarp: return "mA";
                case HudCurvature.DomeProjection: return "mB";
                case HudCurvature.CurvedWorldCanvas: return "mC";
                case HudCurvature.CurvedRt: return "mD";
                default: return null; // Flat (and unknown) edit/read the base layout
            }
        }

        public bool HasModeLayout(HudCurvature mode)
        {
            var k = ModeKey(mode);
            return k != null && GetB(k + "L", false);
        }

        // Resolution honouring BOTH overrides: bare wins when bare (mode-independent); otherwise a
        // live per-mode override wins; otherwise the base fields.
        public HudAnchor AnchorFor(bool bare, HudCurvature mode)
        {
            if (bare && HasBareLayout) return (HudAnchor)GetI("bAnchor", (int)Anchor);
            var k = ModeKey(mode);
            return k != null && GetB(k + "L", false) ? (HudAnchor)GetI(k + "Anchor", (int)Anchor) : Anchor;
        }
        public float XFor(bool bare, HudCurvature mode)
        {
            if (bare && HasBareLayout) return GetF("bX", X);
            var k = ModeKey(mode);
            return k != null && GetB(k + "L", false) ? GetF(k + "X", X) : X;
        }
        public float YFor(bool bare, HudCurvature mode)
        {
            if (bare && HasBareLayout) return GetF("bY", Y);
            var k = ModeKey(mode);
            return k != null && GetB(k + "L", false) ? GetF(k + "Y", Y) : Y;
        }
        public float WFor(bool bare, HudCurvature mode)
        {
            if (bare && HasBareLayout) return Mathf.Max(2f, GetF("bW", W));
            var k = ModeKey(mode);
            return k != null && GetB(k + "L", false) ? Mathf.Max(2f, GetF(k + "W", W)) : W;
        }
        public float HFor(bool bare, HudCurvature mode)
        {
            if (bare && HasBareLayout) return Mathf.Max(2f, GetF("bH", H));
            var k = ModeKey(mode);
            return k != null && GetB(k + "L", false) ? Mathf.Max(2f, GetF(k + "H", H)) : H;
        }
        public float WPctFor(bool bare, HudCurvature mode)
        {
            if (bare && HasBareLayout) return GetF("bWPct", WPct);
            var k = ModeKey(mode);
            return k != null && GetB(k + "L", false) ? GetF(k + "WPct", WPct) : WPct;
        }
        public float HPctFor(bool bare, HudCurvature mode)
        {
            if (bare && HasBareLayout) return GetF("bHPct", HPct);
            var k = ModeKey(mode);
            return k != null && GetB(k + "L", false) ? GetF(k + "HPct", HPct) : HPct;
        }

        /// <summary>Turn a per-mode LIVE override on (seeded from the base geometry, like the bare
        /// one) or off (revert that mode to the base layout). No-op for Flat, which IS the base.</summary>
        public void SetModeLayout(HudCurvature mode, bool on)
        {
            var k = ModeKey(mode);
            if (k == null) return;
            if (on)
            {
                if (GetB(k + "L", false)) return;
                SetB(k + "L", true);
                SetI(k + "Anchor", (int)Anchor);
                SetF(k + "X", X); SetF(k + "Y", Y);
                SetF(k + "W", W); SetF(k + "H", H);
                SetF(k + "WPct", WPct); SetF(k + "HPct", HPct);
            }
            else
            {
                Set(k + "L", null); Set(k + "Anchor", null);
                Set(k + "X", null); Set(k + "Y", null);
                Set(k + "W", null); Set(k + "H", null);
                Set(k + "WPct", null); Set(k + "HPct", null);
            }
        }

        // Write helpers keyed by tier AND mode: bare targets the bare override; else a non-Flat
        // mode targets that mode's live override (auto-enabling it); else the base fields.
        public void SetAnchorFor(bool bare, HudCurvature mode, HudAnchor a)
        {
            if (bare) { SetBareLayout(true); SetI("bAnchor", (int)a); return; }
            var k = ModeKey(mode);
            if (k != null) { SetModeLayout(mode, true); SetI(k + "Anchor", (int)a); } else Anchor = a;
        }
        public void SetXFor(bool bare, HudCurvature mode, float v)
        {
            if (bare) { SetBareLayout(true); SetF("bX", v); return; }
            var k = ModeKey(mode);
            if (k != null) { SetModeLayout(mode, true); SetF(k + "X", v); } else X = v;
        }
        public void SetYFor(bool bare, HudCurvature mode, float v)
        {
            if (bare) { SetBareLayout(true); SetF("bY", v); return; }
            var k = ModeKey(mode);
            if (k != null) { SetModeLayout(mode, true); SetF(k + "Y", v); } else Y = v;
        }
        public void SetWFor(bool bare, HudCurvature mode, float v)
        {
            if (bare) { SetBareLayout(true); SetF("bW", v); return; }
            var k = ModeKey(mode);
            if (k != null) { SetModeLayout(mode, true); SetF(k + "W", v); } else W = v;
        }
        public void SetHFor(bool bare, HudCurvature mode, float v)
        {
            if (bare) { SetBareLayout(true); SetF("bH", v); return; }
            var k = ModeKey(mode);
            if (k != null) { SetModeLayout(mode, true); SetF(k + "H", v); } else H = v;
        }
        public void SetWPctFor(bool bare, HudCurvature mode, float v)
        {
            if (bare) { SetBareLayout(true); SetF("bWPct", v); return; }
            var k = ModeKey(mode);
            if (k != null) { SetModeLayout(mode, true); SetF(k + "WPct", v); } else WPct = v;
        }
        public void SetHPctFor(bool bare, HudCurvature mode, float v)
        {
            if (bare) { SetBareLayout(true); SetF("bHPct", v); return; }
            var k = ModeKey(mode);
            if (k != null) { SetModeLayout(mode, true); SetF(k + "HPct", v); } else HPct = v;
        }

        /// <summary>The point on the element's own box that its anchor pins to, in canvas space
        /// (centre origin, +y up). Half-extents are passed in so both the layout host and the
        /// editor's handle maths share one definition of "where the anchor sits".</summary>
        public Vector2 AnchorPoint(float halfW, float halfH) => AnchorPoint(Anchor, halfW, halfH);

        /// <summary>Anchor-point for an EXPLICIT anchor — used by the per-tier layout resolve so
        /// a bare override can pin to a different screen region than the base.</summary>
        public static Vector2 AnchorPoint(HudAnchor anchor, float halfW, float halfH)
        {
            switch (anchor)
            {
                case HudAnchor.TopLeft: return new Vector2(-halfW, halfH);
                case HudAnchor.TopCenter: return new Vector2(0f, halfH);
                case HudAnchor.TopRight: return new Vector2(halfW, halfH);
                case HudAnchor.MiddleLeft: return new Vector2(-halfW, 0f);
                case HudAnchor.MiddleRight: return new Vector2(halfW, 0f);
                case HudAnchor.BottomLeft: return new Vector2(-halfW, -halfH);
                case HudAnchor.BottomCenter: return new Vector2(0f, -halfH);
                case HudAnchor.BottomRight: return new Vector2(halfW, -halfH);
                default: return Vector2.zero; // Center
            }
        }
    }

    /// <summary>One key/value tunable in an element's param bag. Short attribute names (K/V)
    /// keep the profile XML compact — these repeat a lot.</summary>
    public class HudParam
    {
        [XmlAttribute("K")] public string K;
        [XmlAttribute("V")] public string V;
    }
}
