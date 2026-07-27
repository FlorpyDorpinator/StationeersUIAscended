using System;
using System.Collections.Generic;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// How a <see cref="HudProp"/> wants to be edited. The editor window maps each kind to a
    /// widget (checkbox, slider, combo, colour swatch…) — the descriptor itself never draws.
    /// </summary>
    public enum HudPropKind
    {
        Bool, Float, Int, Text, Enum, ColorRef, Anchor, TierMask, Points, Header,
        /// <summary>A NESTED tab bar. Its <see cref="HudProp.Children"/> are <see cref="TabPage"/>
        /// props; only the selected page's own children draw. Lets a widget fold a long repeated
        /// block (e.g. one page per sense) into a compact, browsable section instead of a wall of rows.</summary>
        TabGroup,
        /// <summary>One page of a <see cref="TabGroup"/>: <see cref="HudProp.Label"/> is the tab
        /// caption and <see cref="HudProp.Children"/> are the props shown while it is selected.
        /// Never drawn on its own — only inside a TabGroup.</summary>
        TabPage,
        /// <summary>A dimmed, non-interactive line of text. Unlike <see cref="Header"/> it draws
        /// no separator and no accent colour, so it reads as a FOOTNOTE to the row above rather
        /// than as the start of a new section — which is what a "shared global" pointer or a
        /// read-only live value is.</summary>
        Note,
        /// <summary>A clickable action row. Carries no value at all: it invokes
        /// <see cref="HudProp.Action"/> and is skipped by every value path (seeding, per-tier
        /// forking, undo bracketing).</summary>
        Button,
    }

    /// <summary>Inspector destination for a property. Widget-specific descriptors default to
    /// Content; HudElementView marks its shared ranges explicitly so F9 can present a short,
    /// capability-aware tabbed inspector without teaching the window about every widget type.</summary>
    public enum HudPropGroup { Content, Layout, Appearance, Effects, Interaction }

    /// <summary>
    /// A single editable knob on a HUD widget, described as data rather than code.
    ///
    /// The point of the delegate pair is to let a widget expose its own live fields to a
    /// *generic* editor without reflection: the editor only ever sees <see cref="Kind"/>,
    /// a label, and boxed get/set — it never needs to know the widget's concrete type.
    /// All the boxing/unboxing lives in the factories below, so every caller stays strongly
    /// typed and the generic side deals only in <see cref="object"/>.
    ///
    /// Deliberately dumb: no ImGui, no rendering, no validation beyond the Min/Max hints.
    /// </summary>
    public sealed class HudProp
    {
        /// <summary>Human-facing name shown next to the widget in the editor.</summary>
        public string Label;

        /// <summary>Which editor widget to draw.</summary>
        public HudPropKind Kind;

        /// <summary>Stable inspector tab. Content is the safe default for widget-owned props.</summary>
        public HudPropGroup Group = HudPropGroup.Content;

        /// <summary>Optional explicit ImGui identity. When absent the drawer uses Group + Label,
        /// which stays stable while inheritance hides or reveals neighbouring rows.</summary>
        public string StableId;

        /// <summary>Slider bounds for <see cref="HudPropKind.Float"/> / <see cref="HudPropKind.Int"/>.
        /// Ignored by the other kinds.</summary>
        public float Min, Max;

        /// <summary>Combo entries for <see cref="HudPropKind.Enum"/>; null otherwise.</summary>
        public string[] EnumNames;

        /// <summary>Reads the current value, boxed. Set by the factories.</summary>
        public Func<object> Get;

        /// <summary>Writes a new value, unboxing to the concrete type. Set by the factories.</summary>
        public Action<object> Set;

        /// <summary>Optional one-line tooltip. Null when the label is self-explanatory.</summary>
        public string Help;

        /// <summary>Runtime fallback for an empty colour reference. Without this, the inspector
        /// cannot know that (for example) an empty warning-bar ref means HudWarn rather than white.</summary>
        public Func<UnityEngine.Color> ColorFallback;

        /// <summary>What a <see cref="HudPropKind.Button"/> row does when clicked. Null for every
        /// other kind. Deliberately NOT routed through Get/Set: a button has no value, so the
        /// undo bracketing and the per-tier seeding walk both skip it.</summary>
        public Action Action;

        /// <summary>Nested props, for the container kinds only (<see cref="HudPropKind.TabGroup"/>
        /// and <see cref="HudPropKind.TabPage"/>). Null for every leaf kind. Children are drawn by
        /// their container and ignore <see cref="Group"/> — the container's group already placed them.</summary>
        public List<HudProp> Children;

        public static HudProp Bool(string label, Func<bool> get, Action<bool> set)
        {
            return new HudProp
            {
                Label = label,
                Kind = HudPropKind.Bool,
                Get = () => get(),
                Set = v => set((bool)v),
            };
        }

        public static HudProp F(string label, Func<float> get, Action<float> set, float min, float max)
        {
            return new HudProp
            {
                Label = label,
                Kind = HudPropKind.Float,
                Min = min,
                Max = max,
                Get = () => get(),
                Set = v => set((float)v),
            };
        }

        public static HudProp I(string label, Func<int> get, Action<int> set, int min, int max)
        {
            return new HudProp
            {
                Label = label,
                Kind = HudPropKind.Int,
                Min = min,
                Max = max,
                Get = () => get(),
                Set = v => set((int)v),
            };
        }

        public static HudProp Text(string label, Func<string> get, Action<string> set)
        {
            return new HudProp
            {
                Label = label,
                Kind = HudPropKind.Text,
                Get = () => get(),
                Set = v => set((string)v),
            };
        }

        public static HudProp Enum(string label, Func<int> get, Action<int> set, string[] names)
        {
            return new HudProp
            {
                Label = label,
                Kind = HudPropKind.Enum,
                EnumNames = names,
                Get = () => get(),
                Set = v => set((int)v),
            };
        }

        /// <summary>A colour reference string: a palette entry name or a "#RRGGBBAA" literal.
        /// Resolved to a real colour by <see cref="HudPalette.Resolve"/>.</summary>
        public static HudProp Color(string label, Func<string> get, Action<string> set,
            Func<UnityEngine.Color> fallback = null)
        {
            return new HudProp
            {
                Label = label,
                Kind = HudPropKind.ColorRef,
                Get = () => get(),
                Set = v => set((string)v),
                ColorFallback = fallback,
            };
        }

        public static HudProp Anchor(string label, Func<int> get, Action<int> set)
        {
            return new HudProp
            {
                Label = label,
                Kind = HudPropKind.Anchor,
                Get = () => get(),
                Set = v => set((int)v),
            };
        }

        public static HudProp Tier(string label, Func<int> get, Action<int> set)
        {
            return new HudProp
            {
                Label = label,
                Kind = HudPropKind.TierMask,
                Get = () => get(),
                Set = v => set((int)v),
            };
        }

        /// <summary>A non-interactive section divider — groups a run of props under a heading
        /// (e.g. the per-element "Effects" block). No get/set.</summary>
        public static HudProp Header(string label)
        {
            return new HudProp { Label = label, Kind = HudPropKind.Header };
        }

        /// <summary>A dimmed footnote line: a shared-global pointer, a read-only live value, a
        /// gating caveat. Draws under the row it annotates with no separator, so it does not read
        /// as a new section the way <see cref="Header"/> does.</summary>
        public static HudProp Note(string label, string help = null)
        {
            return new HudProp { Label = label, Kind = HudPropKind.Note, Help = help };
        }

        /// <summary>A clickable action row (no value, no undo bracket). <paramref name="id"/> must
        /// be unique within the element's prop list — several rows share the same caption.</summary>
        public static HudProp Button(string label, string id, Action action, string help = null)
        {
            return new HudProp
            {
                Label = label,
                Kind = HudPropKind.Button,
                StableId = id,
                Action = action,
                Help = help,
            };
        }

        /// <summary>One page of a nested tab bar. <paramref name="label"/> is the tab caption.</summary>
        public static HudProp TabPage(string label, List<HudProp> children)
        {
            return new HudProp { Label = label, Kind = HudPropKind.TabPage, Children = children };
        }

        /// <summary>A nested tab bar over <paramref name="pages"/> (each a <see cref="TabPage"/>).
        /// Only the selected page's props draw, so a long repeated block stays compact.
        /// <paramref name="id"/> only has to be unique within the element's prop list.</summary>
        public static HudProp TabGroup(string id, List<HudProp> pages)
        {
            return new HudProp { Label = id, Kind = HudPropKind.TabGroup, Children = pages };
        }
    }
}
