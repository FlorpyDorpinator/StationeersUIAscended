using System;

namespace StationeersUIMod.UI.Hud
{
    /// <summary>
    /// How a <see cref="HudProp"/> wants to be edited. The editor window maps each kind to a
    /// widget (checkbox, slider, combo, colour swatch…) — the descriptor itself never draws.
    /// </summary>
    public enum HudPropKind { Bool, Float, Int, Text, Enum, ColorRef, Anchor, TierMask, Points }

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
        public static HudProp Color(string label, Func<string> get, Action<string> set)
        {
            return new HudProp
            {
                Label = label,
                Kind = HudPropKind.ColorRef,
                Get = () => get(),
                Set = v => set((string)v),
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
    }
}
