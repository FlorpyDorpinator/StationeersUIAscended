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
        Portrait, BodyDoll, SuitChips, BareSenses, Vignette
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
        Health, O2Quality, Heading
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

            int dropped = 0, mintedId = 0, clamped = 0, colorFixed = 0, tierFixed = 0;
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
            }

            string label = string.IsNullOrEmpty(Name) ? "HudDocument" : "HudDocument '" + Name + "'";
            if (dropped > 0) UIALog.Warn($"{label}: dropped {dropped} null element(s).");
            if (mintedId > 0) UIALog.Warn($"{label}: minted {mintedId} missing/duplicate element Id(s).");
            if (clamped > 0) UIALog.Warn($"{label}: clamped {clamped} sub-minimum element size(s) to 2px.");
            if (colorFixed > 0) UIALog.Warn($"{label}: defaulted {colorFixed} empty colour reference(s).");
            if (tierFixed > 0) UIALog.Warn($"{label}: reset {tierFixed} element(s) with no visible tier to All.");
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

        /// <summary>The point on the element's own box that its anchor pins to, in canvas space
        /// (centre origin, +y up). Half-extents are passed in so both the layout host and the
        /// editor's handle maths share one definition of "where the anchor sits".</summary>
        public Vector2 AnchorPoint(float halfW, float halfH)
        {
            switch (Anchor)
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
