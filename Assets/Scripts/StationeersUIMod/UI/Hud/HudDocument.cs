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

    /// <summary>Which STYLE slot an element resolves its LOOK against. <see cref="Base"/> is the
    /// suited/robot design every slot inherits from; <see cref="Bare"/> and <see cref="Robot"/> are
    /// OPTIONAL forks an element opts into (see <see cref="HudElementDef.TierStyleMask"/>) — until
    /// then every slot resolves to Base, so a profile that never opts in is byte-identical.
    ///
    /// The stored prefix is exactly two characters ("b_" / "r_"), which keeps the zero-alloc param
    /// scan in <c>HudElementDef.FindSlotOverride</c> a pure length+char compare on the draw path.</summary>
    public enum HudStyleSlot { Base = 0, Bare = 1, Robot = 2 }

    /// <summary>The concrete widget a <see cref="HudElementDef"/> instantiates. Primitives
    /// (Box/Label/Polyline/Icon) are author-drawn; the rest are bespoke views that bind to
    /// live game state. Order is append-only — the int is not persisted, but reordering would
    /// still churn diffs in hand-edited profiles, so new types go on the end.</summary>
    public enum HudElementType
    {
        Box, Label, Polyline, Icon,
        Readout, Clock, WorldName, DayCounter, ActiveHandBadge, Compass,
        MoodletDashboard, EquipmentColumn, HandBoxes, KeybindChips,
        // (A 'Vignette' member sat here until 0.9.2.5 — never constructible from the editor,
        // never present in any shipped profile, and mapped to no widget. The visor-edge
        // vignette is a HudSystem-owned screen overlay, not a document element.)
        Portrait, BodyDoll, SuitChips, BareSenses,
        // Glassy 2.0 additions (append-only — see enum note above):
        VitalsPanel, DamageDoll, JetpackBox, StateChips,
        // A body doll assembled from user PNG art (config/StationeersUIMod/HudIcons), each
        // part tinted per-region by damage. Append-only.
        PngDoll,
        // A freeform FILLED glass shape drawn with the F9 pen tool: an arbitrary closed (and
        // possibly concave) contour rendered by PolygonPanelGraphic. Append-only.
        Shape
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
        /// <summary>The schema the CURRENT code writes. 15 = the 0.9.2.5 (Wave C) opt-in per-tier
        /// style slots; 16 = the 2026-07-26 style-parity Phase 0 (ONE missing-key convention for
        /// the SDF halo family, plus the back-fill that freezes what existing Custom elements
        /// render today — see <c>HudStyleMigration.BackfillSdfKeys</c>). Deliberately NOT stamped
        /// onto every loaded profile by <see cref="Sanitize"/>: the shipped-default replacement
        /// gates in <c>HudSystem.EnsureActiveDocument</c> read the stored value, and every repair
        /// is self-gating (the Wave C adoption keys on the ABSENCE of "tierStyle"; the Phase 0b
        /// back-fill keys on the absence of each SDF key), so stamping would buy nothing and
        /// silently disable those gates.</summary>
        public const int CurrentSchema = 16;

        /// <summary>Bumped when the on-disk shape changes incompatibly; lets a future load
        /// path migrate old profiles instead of silently mis-reading them.</summary>
        [XmlAttribute] public int Schema = 1;

        [XmlAttribute] public string Name;

        /// <summary>Optional per-profile TMP font name (substring match). Empty/null = inherit the
        /// global <see cref="HudConfig.FontName"/>. Lets a shipped "default UI" ship its own type
        /// face without changing the global setting. Resolved in <see cref="HudText"/>.</summary>
        [XmlAttribute] public string Font;

        /// <summary>Optional one-line blurb shown on the profile's card in the Control Center.</summary>
        [XmlAttribute] public string Description;

        /// <summary>Optional author credit shown on the profile's card.</summary>
        [XmlAttribute] public string Author;

        /// <summary>The screen resolution this layout was DESIGNED at. 0 (unset) = fall back to the
        /// player's global reference (HudConfig.HudRefWidth/Height, default 1920x1080).
        ///
        /// This lives in the PROFILE, not the config, because profiles are shareable: element offsets
        /// and sizes are reference pixels, so a layout built on a 2560x1440 screen only reads correctly
        /// elsewhere if the resolution it was authored at travels WITH it. With this set, everyone gets
        /// the author's proportions — a 1920x1080 player renders the same layout at x0.75.
        /// See <see cref="HudConfig.EffectiveHudScale"/>.</summary>
        [XmlAttribute] public float RefW;
        [XmlAttribute] public float RefH;

        [XmlElement("El")] public List<HudElementDef> Elements = new List<HudElementDef>();

        /// <summary>Transient: set by <see cref="Sanitize"/> when the style migration rewrote
        /// elements, so the store can write the repaired XML back ONCE. Without the write-back
        /// the migration would re-run on every load and re-freeze Custom snapshots from
        /// whatever the F9 globals happen to be THAT session (adversarial review 2026-07-17:
        /// "frozen" values silently drifted across sessions). Never serialized.</summary>
        [XmlIgnore] public bool RepairedOnLoad;

        /// <summary>One captured global setting (a colour, an effect toggle/slider, curvature…),
        /// stored as its config key + serialized value. See <see cref="HudTheme"/>.</summary>
        public sealed class ThemeEntry
        {
            [XmlAttribute("k")] public string K;
            [XmlAttribute("v")] public string V;
        }

        /// <summary>This profile's THEME: a snapshot of the GLOBAL look (HUD palette + radial
        /// palette + all global effects/curvature/sizing) captured by <see cref="HudTheme"/>.
        /// Null/empty on an older or never-themed profile — that profile then just uses whatever
        /// globals are current (the pre-theme behaviour), so nothing regresses. Applied on load so
        /// switching profiles restores the whole look, not only the element layout.</summary>
        [XmlArray("Theme")]
        [XmlArrayItem("E")]
        public List<ThemeEntry> Theme;

        /// <summary>Deep copy WITHOUT a serialize/deserialize round-trip. The editor's undo
        /// stack snapshots the whole document on every drag, so this runs many times a
        /// session — an XmlSerializer round-trip per snapshot would be needless GC and CPU.</summary>
        public HudDocument Clone()
        {
            var copy = new HudDocument
            {
                Schema = Schema, Name = Name, Font = Font, Description = Description, Author = Author,
                RefW = RefW, RefH = RefH,
                Theme = HudTheme.CopyOf(Theme), // the captured global look travels with an undo clone
            };
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
            int tierAdopted = 0;
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

                // Wave C (0.9.2.5) legacy adoption: a profile authored BEFORE the per-tier style
                // fork became opt-in carries a SPARSE set of "b_" overrides and no "tierStyle"
                // key. Adopt it — set the Bare fork bit so those overrides keep resolving exactly
                // as they did, and flag the element so HudSystem can COMPLETE the fork from the
                // base on the first view build (the seed needs the widget's own prop list, which
                // the document layer cannot see). Idempotent: adoption writes tierStyle, and the
                // condition requires tierStyle to be absent.
                if (el.GetS(HudElementDef.TierStyleKey, null) == null
                    && el.HasAnySlotOverride(HudStyleSlot.Bare))
                {
                    el.SetI(HudElementDef.TierStyleKey, HudElementDef.ForkBare);
                    el.SetB(HudElementDef.TierStyleSeedKey, true);
                    tierAdopted++;
                }

                // A per-mode BARE fork (layout OR the visual slots: colour/sizing/glass/effects)
                // only means something for a "Both" element (shown in bare AND a live tier). If the
                // element is single-mode (Bare-only or Live-only), those overrides are dead data —
                // strip them so they can't bloat the profile or desync a drag (render vs edit
                // tier). Idempotent, so it settles after one save.
                bool both = (el.Tiers & HudTierMask.Bare) != 0
                         && (el.Tiers & (HudTierMask.Suited | HudTierMask.Robot)) != 0;
                if (!both && (el.HasBareLayout || el.HasAnyVisualBareOverride()))
                {
                    el.SetBareLayout(false);
                    el.ClearVisualBareOverrides();
                    bareOrphan++;
                }
                else if (el.ForksSlot(HudStyleSlot.Robot) && (el.Tiers & HudTierMask.Robot) == 0)
                {
                    // Same rule for the ROBOT slot: no robot tier, no robot style.
                    el.SetForkSlot(HudStyleSlot.Robot, false);
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

            // The 2026-07-19 transition tri-state: the per-element power-transition flags were
            // plain bools defaulting TRUE, so "inherit the global" and "explicitly on" were the
            // same stored value and "off" could not be expressed at all. Fold every stored legacy
            // bool into "<key>Mode" (0 Inherit / 1 On / 2 Off) — an explicit "off" ALWAYS survives.
            // Idempotent (a stored mode is never rewritten) and writes nothing for elements that
            // never had a flag, so untouched profiles stay byte-identical. Fail-soft per element.
            int fxModes = 0;
            for (int i = 0; i < Elements.Count; i++)
            {
                var el = Elements[i];
                if (el == null) continue;
                try { fxModes += HudTransitionFx.MigrateElement(el); }
                catch (Exception e)
                {
                    UIALog.Warn("HudDocument: transition-mode migration failed on element '"
                        + el.Id + "' (" + e.Message + ") — left on the legacy flag.");
                }
            }
            if (fxModes > 0) RepairedOnLoad = true;

            // The 2026-07-16 style regression: rewrite every pre-standardisation ("legacy
            // mixed") element into the coherent two-state contract. Idempotent (keyed on the
            // stored styleSource) and fail-soft per element; see HudStyleMigration.
            int styleMigrated = 0;
            try { styleMigrated = HudStyleMigration.Migrate(this); }
            catch (Exception e) { UIALog.Warn("HudDocument: style migration failed (" + e.Message + ")."); }
            if (styleMigrated > 0) RepairedOnLoad = true;

            // Schema 16 (2026-07-26, style-parity Phase 0b): the SDF halo family now resolves an
            // ABSENT per-element key to the GLOBAL instead of to a fixed neutral — one convention
            // for every knob. Freeze what already-Custom elements render TODAY before that rule
            // can hand them a feature they never asked for. Runs AFTER the legacy migration above,
            // which may itself have just written a complete Custom snapshot. Self-gating on the
            // absence of each key, per style slot; fail-soft per element.
            int sdfBackfilled = 0, sdfElements = 0;
            for (int i = 0; i < Elements.Count; i++)
            {
                var el = Elements[i];
                if (el == null) continue;
                try
                {
                    int n = HudStyleMigration.BackfillSdfKeys(el);
                    if (n > 0) { sdfBackfilled += n; sdfElements++; }
                }
                catch (Exception e)
                {
                    UIALog.Warn("HudDocument: SDF back-fill failed on element '"
                        + el.Id + "' (" + e.Message + ") — left on the resolver fallback.");
                }
            }
            if (sdfBackfilled > 0) RepairedOnLoad = true;

            string label = string.IsNullOrEmpty(Name) ? "HudDocument" : "HudDocument '" + Name + "'";
            if (sdfBackfilled > 0)
                UIALog.Warn($"{label}: back-filled {sdfBackfilled} missing halo key(s) on {sdfElements} custom-styled element(s) from the current globals.");
            if (styleMigrated > 0)
                UIALog.Warn($"{label}: migrated {styleMigrated} legacy-styled element(s) to the two-state style contract.");
            if (fxModes > 0)
                UIALog.Warn($"{label}: folded {fxModes} legacy transition flag(s) into the Inherit/On/Off tri-state.");
            if (dropped > 0) UIALog.Warn($"{label}: dropped {dropped} null element(s).");
            if (mintedId > 0) UIALog.Warn($"{label}: minted {mintedId} missing/duplicate element Id(s).");
            if (clamped > 0) UIALog.Warn($"{label}: clamped {clamped} sub-minimum element size(s) to 2px.");
            if (colorFixed > 0) UIALog.Warn($"{label}: defaulted {colorFixed} empty colour reference(s).");
            if (tierFixed > 0) UIALog.Warn($"{label}: reset {tierFixed} element(s) with no visible tier to All.");
            if (speedFixed > 0) UIALog.Warn($"{label}: made {speedFixed} Speed readout(s) suit-only (were showing in bare).");
            if (bareOrphan > 0) UIALog.Warn($"{label}: cleared {bareOrphan} orphaned bare-layout override(s) (element not shown in bare).");
            if (tierAdopted > 0)
            {
                RepairedOnLoad = true;
                UIALog.Warn($"{label}: adopted {tierAdopted} legacy bare override(s) into the per-tier fork.");
            }
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

        // ---- per-tier VISUAL STYLE SLOTS: colours, sizing, glass and effects ----------------
        // The layout override above lets a "Both" element sit somewhere different in bare; this
        // extends the SAME idea to how it LOOKS. Every visual property can be forked per TIER —
        // Fill, Border, corners, glass sheen, glow, frost, every effect knob, every widget-owned
        // appearance param — under a two-character slot prefix ("b_" bare, "r_" robot),
        // deliberately distinct from the layout keys ("bX"/"bLayout", no underscore).
        //
        // 0.9.2.5 (Wave C) generalised the old hardcoded "b_" family into these slots AND made the
        // fork OPT-IN. The pre-Wave-C model was a SPARSE overlay on a SINGLE SHARED BASE where
        // "suited" WAS the base, so any key the author never happened to fork silently leaked
        // between tiers (FlorpyDorp: "the hand / 1-6 colours don't switch back"). Now:
        //   • an element opts in per slot via the "tierStyle" bitmask (0 = shared, the default);
        //   • enabling a fork SEEDS it with a complete copy of the base's STORED values
        //     (HudElementView.SeedSlotFromBase), so later base edits cannot leak into it;
        //   • every READ resolves through <see cref="ResolveSlot"/> first, so stale "b_"/"r_"
        //     residue on an element that is not opted in can never resurrect.
        // A profile that never opts in writes no new key and renders identically.
        //
        // Reads are ZERO-ALLOC and skip all extra work in the common (Base) path — the visor is
        // only bare when powered down, so the suited HUD pays nothing. A malformed override
        // degrades to the base value rather than throwing (rule 5).

        /// <summary>Param key holding the per-element opt-in bitmask (see <see cref="ForkBare"/> /
        /// <see cref="ForkRobot"/>). Absent/0 = one shared style for every tier.</summary>
        public const string TierStyleKey = "tierStyle";

        /// <summary>TRANSIENT param written by <see cref="HudDocument.Sanitize"/> when it adopts a
        /// LEGACY sparse bare fork into the opt-in model: the seed itself needs the widget's own
        /// prop list, which the document layer cannot see, so HudSystem completes it on the first
        /// view build after load and deletes the key.</summary>
        public const string TierStyleSeedKey = "tierStyleNeedsSeed";

        public const int ForkBare = 1;
        public const int ForkRobot = 2;

        /// <summary>The slot's stored prefix CHARACTER ('\0' for Base, which has no prefix).</summary>
        internal static char SlotPrefixChar(HudStyleSlot s)
        {
            switch (s)
            {
                case HudStyleSlot.Bare: return 'b';
                case HudStyleSlot.Robot: return 'r';
                default: return '\0';
            }
        }

        /// <summary>The slot's stored key prefix. Only used by WRITERS (edit time), never on the
        /// draw path — reads match the prefix character in place instead of concatenating.</summary>
        internal static string SlotPrefix(HudStyleSlot s)
        {
            switch (s)
            {
                case HudStyleSlot.Bare: return "b_";
                case HudStyleSlot.Robot: return "r_";
                default: return "";
            }
        }

        /// <summary>Which slots this element forks. 0 (the default) = one shared style.</summary>
        public int TierStyleMask => GetI(TierStyleKey, 0);

        /// <summary>True when this element carries its OWN style for <paramref name="s"/>.
        /// Base is never a fork — it IS what the others inherit from.</summary>
        public bool ForksSlot(HudStyleSlot s)
        {
            if (s == HudStyleSlot.Base) return false;
            int bit = s == HudStyleSlot.Bare ? ForkBare : ForkRobot;
            return (TierStyleMask & bit) != 0;
        }

        /// <summary>The slot a read actually resolves against: the wanted slot when this element
        /// forks it, otherwise Base. EVERY read accessor runs this first, which is what makes stale
        /// override residue on a non-opted-in element inert instead of resurrecting.</summary>
        public HudStyleSlot ResolveSlot(HudStyleSlot want)
            => ForksSlot(want) ? want : HudStyleSlot.Base;

        /// <summary>Turn a slot fork on or off. Turning it OFF drops that slot's stored overrides so
        /// the profile shrinks back; turning it ON only sets the bit — the caller
        /// (<c>HudElementView.SeedSlotFromBase</c>) fills the slot from the base, because a complete
        /// copy needs the widget's own prop list. Idempotent.</summary>
        public void SetForkSlot(HudStyleSlot s, bool on)
        {
            if (s == HudStyleSlot.Base) return;
            int bit = s == HudStyleSlot.Bare ? ForkBare : ForkRobot;
            int next = on ? (TierStyleMask | bit) : (TierStyleMask & ~bit);
            if (!on) ClearSlotOverrides(s);
            if (next == 0) { Set(TierStyleKey, null); Set(TierStyleSeedKey, null); }
            else SetI(TierStyleKey, next);
        }

        /// <summary>Zero-alloc lookup of the "&lt;prefix&gt;_"+key override WITHOUT concatenating a
        /// string on the draw path (matches the "no per-frame alloc" discipline of the bag
        /// accessors). Returns null for Base, which has no prefix.</summary>
        private HudParam FindSlotOverride(HudStyleSlot s, string key)
        {
            char pfx = SlotPrefixChar(s);
            if (pfx == '\0' || Params == null || key == null) return null;
            int kl = key.Length;
            for (int i = 0; i < Params.Count; i++)
            {
                var p = Params[i];
                if (p == null || p.K == null) continue;
                string k = p.K;
                if (k.Length == kl + 2 && k[0] == pfx && k[1] == '_'
                    && string.CompareOrdinal(k, 2, key, 0, kl) == 0)
                    return p;
            }
            return null;
        }

        /// <summary>RAW presence test (no <see cref="ResolveSlot"/>): does this element STORE an
        /// override for <paramref name="key"/> in <paramref name="s"/>? Used by the editor's forked-
        /// property marker, by Sanitize's legacy adoption and by the transition migration.</summary>
        public bool HasSlotOverride(HudStyleSlot s, string key) => FindSlotOverride(s, key) != null;

        /// <summary>RAW presence test: does this element store ANY override in <paramref name="s"/>?</summary>
        public bool HasAnySlotOverride(HudStyleSlot s)
        {
            char pfx = SlotPrefixChar(s);
            if (pfx == '\0' || Params == null) return false;
            for (int i = 0; i < Params.Count; i++)
            {
                var p = Params[i];
                if (p != null && p.K != null && p.K.Length >= 2 && p.K[0] == pfx && p.K[1] == '_')
                    return true;
            }
            return false;
        }

        /// <summary>Drop every stored override in one slot so it reverts to fully inheriting the
        /// base look. Leaves the LAYOUT override family ("bX"/"bLayout", "mA*"…) untouched — those
        /// carry no underscore, which is exactly the discriminator this scan uses.</summary>
        public void ClearSlotOverrides(HudStyleSlot s)
        {
            char pfx = SlotPrefixChar(s);
            if (pfx == '\0' || Params == null) return;
            for (int i = Params.Count - 1; i >= 0; i--)
            {
                var p = Params[i];
                if (p != null && p.K != null && p.K.Length >= 2 && p.K[0] == pfx && p.K[1] == '_')
                    Params.RemoveAt(i);
            }
        }

        /// <summary>Legacy shorthand for the BARE slot (kept: the transition migration and the
        /// editor still speak in "bare").</summary>
        public bool HasBareOverride(string key) => HasSlotOverride(HudStyleSlot.Bare, key);

        /// <summary>True when ANY per-tier visual override or opt-in is present. Cheap gate for
        /// Sanitize's orphan strip and the "reset to inherit" affordance.</summary>
        public bool HasAnyVisualBareOverride()
            => HasAnySlotOverride(HudStyleSlot.Bare) || HasAnySlotOverride(HudStyleSlot.Robot)
               || TierStyleMask != 0;

        /// <summary>Drop every per-tier visual fork (both slots) AND the opt-in mask, so every tier
        /// shares the base look again.</summary>
        public void ClearVisualBareOverrides()
        {
            ClearSlotOverrides(HudStyleSlot.Bare);
            ClearSlotOverrides(HudStyleSlot.Robot);
            Set(TierStyleKey, null);
            Set(TierStyleSeedKey, null);
        }

        // Generic bag accessors, slot-aware. The slot's override wins when the element FORKS that
        // slot and the key is present (and valid); otherwise the base key. Every effect/appearance
        // param a widget stores in the bag forks through these — one code path covers the whole
        // glass/effects family.
        public float GetFFor(HudStyleSlot s, string key, float def)
        {
            var p = FindSlotOverride(ResolveSlot(s), key);
            float v;
            if (p != null && float.TryParse(p.V, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return GetF(key, def);
        }
        public bool GetBFor(HudStyleSlot s, string key, bool def)
        {
            var p = FindSlotOverride(ResolveSlot(s), key);
            bool v;
            if (p != null && bool.TryParse(p.V, out v)) return v;
            return GetB(key, def);
        }
        public int GetIFor(HudStyleSlot s, string key, int def)
        {
            var p = FindSlotOverride(ResolveSlot(s), key);
            int v;
            if (p != null && int.TryParse(p.V, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return GetI(key, def);
        }
        public string GetSFor(HudStyleSlot s, string key, string def)
        {
            var p = FindSlotOverride(ResolveSlot(s), key);
            if (p != null && p.V != null) return p.V;
            return GetS(key, def);
        }

        // COPY-ON-WRITE, the second half of "seed on separation". Seeding writes a complete copy of
        // the base into a fork, but two holes would remain without this:
        //   • a widget that GAINS a knob after the author forked (the fork has no twin for it);
        //   • a base key that was ABSENT at seed time and whose row stores nothing for an empty
        //     value (every "empty ColorRef = use the palette default" row).
        // In both cases a later BASE edit would silently reappear in the fork — the exact defect
        // Wave C exists to remove. So before the base value of a key changes, hand every forked
        // slot that has no override of its own the value it is CURRENTLY resolving.
        //
        // Guarded on TierStyleMask == 0, so an element with no fork (the default) pays one int
        // read; and it only ever runs at edit time, never on the draw path.
        private void ProtectForks(string key, bool stringKey)
        {
            if (key == null || TierStyleMask == 0) return;
            ProtectFork(HudStyleSlot.Bare, key, stringKey);
            ProtectFork(HudStyleSlot.Robot, key, stringKey);
        }

        private void ProtectFork(HudStyleSlot s, string key, bool stringKey)
        {
            if (!ForksSlot(s) || FindSlotOverride(s, key) != null) return;
            var p = Find(key);
            // No stored base value: for a STRING row the absent value IS the empty ref (which every
            // widget reads as "use the palette default"), so "" is a faithful copy. For a numeric or
            // bool row we cannot know the widget's default here — seeding already wrote it, because
            // SetF/SetB/SetI always store — so leave that slot inheriting rather than invent a value.
            if (p == null) { if (stringKey) Set(SlotPrefix(s) + key, ""); return; }
            Set(SlotPrefix(s) + key, p.V);
        }

        // Writers concatenate "<prefix>_"+key, but only ever run at EDIT time (an F9 drag/slide),
        // never on the per-frame draw path, so the concat is harmless. Writers do NOT resolve: the
        // caller already picked the slot (via HudElementView.EditSlot, which resolved it), and the
        // seeding/migration paths deliberately write a slot before its reads are live. A null value
        // removes the override (the shared Set contract) = "this slot inherits the base again".
        //
        // A knob that is deliberately SHARED by every tier (the power-transition registry) writes
        // through the raw Set/SetF/SetB/SetI instead, so it never trips the copy-on-write above.
        public void SetFFor(HudStyleSlot s, string key, float v)
        { if (s == HudStyleSlot.Base) { ProtectForks(key, false); SetF(key, v); } else SetF(SlotPrefix(s) + key, v); }
        public void SetBFor(HudStyleSlot s, string key, bool v)
        { if (s == HudStyleSlot.Base) { ProtectForks(key, false); SetB(key, v); } else SetB(SlotPrefix(s) + key, v); }
        public void SetIFor(HudStyleSlot s, string key, int v)
        { if (s == HudStyleSlot.Base) { ProtectForks(key, false); SetI(key, v); } else SetI(SlotPrefix(s) + key, v); }
        public void SetSFor(HudStyleSlot s, string key, string v)
        { if (s == HudStyleSlot.Base) { ProtectForks(key, true); Set(key, v); } else Set(SlotPrefix(s) + key, v); }

        // Bool call shape kept for the render/edit paths that only ever distinguish bare from the
        // base (LayoutBare / EditBare). It maps straight onto the slot API — so those call sites
        // gained the opt-in gate and the "stale residue stays inert" guarantee for free.
        public float GetFFor(bool bare, string key, float def)
            => GetFFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, key, def);
        public bool GetBFor(bool bare, string key, bool def)
            => GetBFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, key, def);
        public int GetIFor(bool bare, string key, int def)
            => GetIFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, key, def);
        public string GetSFor(bool bare, string key, string def)
            => GetSFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, key, def);
        public void SetFFor(bool bare, string key, float v)
            => SetFFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, key, v);
        public void SetBFor(bool bare, string key, bool v)
            => SetBFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, key, v);
        public void SetIFor(bool bare, string key, int v)
            => SetIFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, key, v);
        public void SetSFor(bool bare, string key, string v)
            => SetSFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, key, v);

        // First-class visual FIELDS (attributes, not bag params) get their own slot overrides,
        // stored in the bag under fixed literals so no string is built on the draw path.
        private HudParam FieldOverride(HudStyleSlot s, string bareKey, string robotKey)
        {
            if (s == HudStyleSlot.Bare) return Find(bareKey);
            if (s == HudStyleSlot.Robot) return Find(robotKey);
            return null;
        }

        private float FieldFloat(HudStyleSlot s, string bareKey, string robotKey, float baseValue)
        {
            var p = FieldOverride(ResolveSlot(s), bareKey, robotKey);
            float v;
            if (p != null && float.TryParse(p.V, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return baseValue;
        }

        public string FillFor(HudStyleSlot s)
        { var p = FieldOverride(ResolveSlot(s), "b_fill", "r_fill"); return p != null && p.V != null ? p.V : Fill; }
        public string BorderFor(HudStyleSlot s)
        { var p = FieldOverride(ResolveSlot(s), "b_border", "r_border"); return p != null && p.V != null ? p.V : Border; }
        public string TextColorFor(HudStyleSlot s)
        { var p = FieldOverride(ResolveSlot(s), "b_text", "r_text"); return p != null && p.V != null ? p.V : TextColor; }
        public float BorderWidthFor(HudStyleSlot s) => FieldFloat(s, "b_bw", "r_bw", BorderWidth);
        public float RTLFor(HudStyleSlot s) => FieldFloat(s, "b_rtl", "r_rtl", RTL);
        public float RTRFor(HudStyleSlot s) => FieldFloat(s, "b_rtr", "r_rtr", RTR);
        public float RBRFor(HudStyleSlot s) => FieldFloat(s, "b_rbr", "r_rbr", RBR);
        public float RBLFor(HudStyleSlot s) => FieldFloat(s, "b_rbl", "r_rbl", RBL);
        public float FontScaleFor(HudStyleSlot s) => FieldFloat(s, "b_fs", "r_fs", FontScale);

        /// <summary>Copy-on-write for a first-class FIELD (see <see cref="ProtectForks"/>): the base
        /// value always exists here, so a forked slot with no twin is handed it verbatim before the
        /// field changes.</summary>
        private void ProtectFieldForks(string bareKey, string robotKey, string current)
        {
            if (TierStyleMask == 0) return;
            if (ForksSlot(HudStyleSlot.Bare) && Find(bareKey) == null) Set(bareKey, current ?? "");
            if (ForksSlot(HudStyleSlot.Robot) && Find(robotKey) == null) Set(robotKey, current ?? "");
        }

        private void ProtectFieldForks(string bareKey, string robotKey, float current)
        {
            if (TierStyleMask == 0) return;
            if (ForksSlot(HudStyleSlot.Bare) && Find(bareKey) == null) SetF(bareKey, current);
            if (ForksSlot(HudStyleSlot.Robot) && Find(robotKey) == null) SetF(robotKey, current);
        }

        public void SetFillFor(HudStyleSlot s, string v)
        { if (s == HudStyleSlot.Bare) Set("b_fill", v); else if (s == HudStyleSlot.Robot) Set("r_fill", v); else { ProtectFieldForks("b_fill", "r_fill", Fill); Fill = v; } }
        public void SetBorderFor(HudStyleSlot s, string v)
        { if (s == HudStyleSlot.Bare) Set("b_border", v); else if (s == HudStyleSlot.Robot) Set("r_border", v); else { ProtectFieldForks("b_border", "r_border", Border); Border = v; } }
        public void SetTextColorFor(HudStyleSlot s, string v)
        { if (s == HudStyleSlot.Bare) Set("b_text", v); else if (s == HudStyleSlot.Robot) Set("r_text", v); else { ProtectFieldForks("b_text", "r_text", TextColor); TextColor = v; } }
        public void SetBorderWidthFor(HudStyleSlot s, float v)
        { if (s == HudStyleSlot.Bare) SetF("b_bw", v); else if (s == HudStyleSlot.Robot) SetF("r_bw", v); else { ProtectFieldForks("b_bw", "r_bw", BorderWidth); BorderWidth = v; } }
        public void SetRTLFor(HudStyleSlot s, float v)
        { if (s == HudStyleSlot.Bare) SetF("b_rtl", v); else if (s == HudStyleSlot.Robot) SetF("r_rtl", v); else { ProtectFieldForks("b_rtl", "r_rtl", RTL); RTL = v; } }
        public void SetRTRFor(HudStyleSlot s, float v)
        { if (s == HudStyleSlot.Bare) SetF("b_rtr", v); else if (s == HudStyleSlot.Robot) SetF("r_rtr", v); else { ProtectFieldForks("b_rtr", "r_rtr", RTR); RTR = v; } }
        public void SetRBRFor(HudStyleSlot s, float v)
        { if (s == HudStyleSlot.Bare) SetF("b_rbr", v); else if (s == HudStyleSlot.Robot) SetF("r_rbr", v); else { ProtectFieldForks("b_rbr", "r_rbr", RBR); RBR = v; } }
        public void SetRBLFor(HudStyleSlot s, float v)
        { if (s == HudStyleSlot.Bare) SetF("b_rbl", v); else if (s == HudStyleSlot.Robot) SetF("r_rbl", v); else { ProtectFieldForks("b_rbl", "r_rbl", RBL); RBL = v; } }
        public void SetFontScaleFor(HudStyleSlot s, float v)
        { if (s == HudStyleSlot.Bare) SetF("b_fs", v); else if (s == HudStyleSlot.Robot) SetF("r_fs", v); else { ProtectFieldForks("b_fs", "r_fs", FontScale); FontScale = v; } }

        // Bool call shape for the first-class fields (see the bag accessors above).
        public string FillFor(bool bare) => FillFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base);
        public string BorderFor(bool bare) => BorderFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base);
        public string TextColorFor(bool bare) => TextColorFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base);
        public float BorderWidthFor(bool bare) => BorderWidthFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base);
        public float RTLFor(bool bare) => RTLFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base);
        public float RTRFor(bool bare) => RTRFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base);
        public float RBRFor(bool bare) => RBRFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base);
        public float RBLFor(bool bare) => RBLFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base);
        public float FontScaleFor(bool bare) => FontScaleFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base);
        public void SetFillFor(bool bare, string v) => SetFillFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, v);
        public void SetBorderFor(bool bare, string v) => SetBorderFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, v);
        public void SetTextColorFor(bool bare, string v) => SetTextColorFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, v);
        public void SetBorderWidthFor(bool bare, float v) => SetBorderWidthFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, v);
        public void SetRTLFor(bool bare, float v) => SetRTLFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, v);
        public void SetRTRFor(bool bare, float v) => SetRTRFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, v);
        public void SetRBRFor(bool bare, float v) => SetRBRFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, v);
        public void SetRBLFor(bool bare, float v) => SetRBLFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, v);
        public void SetFontScaleFor(bool bare, float v) => SetFontScaleFor(bare ? HudStyleSlot.Bare : HudStyleSlot.Base, v);

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
