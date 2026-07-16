using System;
using System.Collections.Generic;
using ImGuiNET;
using StationeersUIMod.UI.Hud;
using UnityEngine;

namespace StationeersUIMod.Windows
{
    /// <summary>
    /// Renders a list of <see cref="HudProp"/> descriptors as ImGui widgets — the one place
    /// that knows how each <see cref="HudPropKind"/> looks on screen, so every panel and the
    /// click-to-edit popup can expose their knobs without writing widget code of their own.
    ///
    /// The drawer stays deliberately ignorant of the document: it only ever calls the prop's
    /// boxed Get/Set and raises begin/commit/cancel callbacks so the CALLER can bracket an edit
    /// for undo without retaining a dead snapshot after an activation that changed nothing. Getting
    /// that bracketing right is the whole point — <see cref="DrawAll"/>'s doc comment spells out
    /// exactly when each callback fires, because undo granularity lives or dies on the timing.
    /// </summary>
    public static class HudPropDrawer
    {
        // The nine anchor names never change, and combos are drawn every frame while the popup
        // is open — build the string array once rather than re-allocating it per frame per prop.
        private static readonly string[] AnchorNames = Enum.GetNames(typeof(HudAnchor));

        // Flag sets mirror the existing HUD colour wheel so a colour ref edited here feels
        // identical to one edited in the F9 palette list.
        private const ImGuiColorEditFlags WheelFlags = ImGuiColorEditFlags.AlphaBar
                                                      | ImGuiColorEditFlags.AlphaPreviewHalf
                                                      | ImGuiColorEditFlags.PickerHueWheel;
        private const ImGuiColorEditFlags SwatchFlags = ImGuiColorEditFlags.AlphaPreviewHalf;

        /// <summary>
        /// Draw every prop in order. <paramref name="onBeginEdit"/> fires ONCE at the start of an
        /// edit gesture — on <c>IsItemActivated</c> for the drag/slide/type widgets, or immediately
        /// before <c>Set</c> for one-shot picks (a checkbox toggle, a combo selection). It is the
        /// caller's cue to snapshot the pre-edit state. <paramref name="onCommitted"/> fires when the
        /// gesture completes — on <c>IsItemDeactivatedAfterEdit</c> for the continuous widgets, or
        /// immediately after <c>Set</c> for the one-shots — the cue to push that snapshot onto the
        /// undo stack and mark the document dirty. <c>onChanged</c> reports that the gesture really
        /// mutated its property, including when a tab switch prevents normal deactivation.
        /// <c>onCancelled</c> drops an activation snapshot
        /// when a continuous item deactivates without editing. A slider dragged for a second is ONE
        /// undo step, not sixty; a checkbox is one step; get either bracket wrong and undo shatters
        /// into per-frame noise.
        /// </summary>
        public static void DrawAll(List<HudProp> props, Action onBeginEdit, Action onCommitted,
            Action onCancelled = null, Action onChanged = null)
            => Draw(props, null, onBeginEdit, onCommitted, onCancelled, onChanged);

        public static void DrawGroup(List<HudProp> props, HudPropGroup group,
            Action onBeginEdit, Action onCommitted, Action onCancelled = null,
            Action onChanged = null)
            => Draw(props, group, onBeginEdit, onCommitted, onCancelled, onChanged);

        public static bool HasGroup(List<HudProp> props, HudPropGroup group)
        {
            if (props == null) return false;
            for (int i = 0; i < props.Count; i++)
                if (props[i] != null && props[i].Group == group) return true;
            return false;
        }

        private static void Draw(List<HudProp> props, HudPropGroup? only,
            Action onBeginEdit, Action onCommitted, Action onCancelled, Action onChanged)
        {
            if (props == null) return;
            for (int i = 0; i < props.Count; i++)
            {
                HudProp p = props[i];
                if (p == null || (only.HasValue && p.Group != only.Value)) continue;
                // Labels repeat across elements (every Box has a "Fill", every Readout an "Anchor"),
                // so the visible label carries a per-index "##hp" suffix — same text, unique ImGui id.
                string key = !string.IsNullOrEmpty(p.StableId)
                    ? p.StableId : p.Group + "_" + p.Label;
                string sfx = "##hp_" + key;

                switch (p.Kind)
                {
                    case HudPropKind.Bool: DrawBool(p, sfx, onBeginEdit, onCommitted, onChanged); break;
                    case HudPropKind.Float: DrawFloat(p, sfx, onBeginEdit, onCommitted, onCancelled, onChanged); break;
                    case HudPropKind.Int: DrawInt(p, sfx, onBeginEdit, onCommitted, onCancelled, onChanged); break;
                    case HudPropKind.Text: DrawText(p, sfx, onBeginEdit, onCommitted, onCancelled, onChanged); break;
                    case HudPropKind.Enum: DrawEnum(p, sfx, onBeginEdit, onCommitted, onChanged); break;
                    case HudPropKind.Anchor: DrawAnchor(p, sfx, onBeginEdit, onCommitted, onChanged); break;
                    case HudPropKind.TierMask: DrawTier(p, key, onBeginEdit, onCommitted, onChanged); break;
                    case HudPropKind.ColorRef: DrawColorRef(p, key, onBeginEdit, onCommitted, onCancelled, onChanged); break;
                    case HudPropKind.Points: DrawPoints(p); break;
                    case HudPropKind.Header:
                        ImGui.Spacing();
                        ImGui.Separator();
                        ImGui.TextColored(new Vector4(0.25f, 0.85f, 0.93f, 1f), p.Label);
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ scalar widgets

        private static void DrawBool(HudProp p, string sfx, Action begin, Action commit,
            Action changed)
        {
            bool v = (bool)p.Get();
            // A checkbox is one-shot: it only reports on the frame it flips, so the whole
            // gesture — begin, set, commit — collapses into that single true return.
            if (ImGui.Checkbox(p.Label + sfx, ref v))
            {
                begin?.Invoke();
                p.Set(v);
                changed?.Invoke();
                commit?.Invoke();
            }
            Tooltip(p);
        }

        private static void DrawFloat(HudProp p, string sfx, Action begin, Action commit,
            Action cancel, Action noteChanged)
        {
            float v = (float)p.Get();
            bool changed = ImGui.SliderFloat(p.Label + sfx, ref v, p.Min, p.Max);
            // SliderFloat may change its value on the same mouse-down frame that activates it.
            // Capture the document after ImGui establishes the item state but BEFORE applying the
            // returned value, otherwise Undo starts at the click position instead of the true old
            // value.
            BeginContinuous(begin);
            if (changed)
            {
                p.Set(v);
                noteChanged?.Invoke();
            }
            EndContinuous(commit, cancel);
            Tooltip(p);
        }

        private static void DrawInt(HudProp p, string sfx, Action begin, Action commit,
            Action cancel, Action noteChanged)
        {
            int v = (int)p.Get();
            bool changed = ImGui.SliderInt(p.Label + sfx, ref v, (int)p.Min, (int)p.Max);
            BeginContinuous(begin);
            if (changed)
            {
                p.Set(v);
                noteChanged?.Invoke();
            }
            EndContinuous(commit, cancel);
            Tooltip(p);
        }

        private static void DrawText(HudProp p, string sfx, Action begin, Action commit,
            Action cancel, Action noteChanged)
        {
            string v = (string)p.Get() ?? "";
            // "Text" in the label means a free-form body (an element's Text field) that wants room to
            // breathe; anything else (an icon name, an align token) is a one-liner.
            bool multiline = p.Label.IndexOf("Text", StringComparison.Ordinal) >= 0;
            bool changed;
            if (multiline)
            {
                changed = ImGui.InputTextMultiline(p.Label + sfx, ref v, 256u, new Vector2(0f, 54f));
            }
            else
            {
                changed = ImGui.InputText(p.Label + sfx, ref v, 256u);
            }
            BeginContinuous(begin);
            if (changed)
            {
                p.Set(v);
                noteChanged?.Invoke();
            }
            EndContinuous(commit, cancel);
            Tooltip(p);
        }

        // ------------------------------------------------------------------ combos

        private static void DrawEnum(HudProp p, string sfx, Action begin, Action commit,
            Action changed)
        {
            string[] names = p.EnumNames;
            if (names == null || names.Length == 0) return;
            int cur = (int)p.Get();
            string preview = cur >= 0 && cur < names.Length ? names[cur] : "";
            if (ImGui.BeginCombo(p.Label + sfx, preview))
            {
                for (int n = 0; n < names.Length; n++)
                {
                    if (!ImGui.Selectable(names[n], n == cur) || n == cur) continue;
                    begin?.Invoke();
                    p.Set(n);
                    changed?.Invoke();
                    commit?.Invoke();
                }
                ImGui.EndCombo();
            }
            Tooltip(p);
        }

        private static void DrawAnchor(HudProp p, string sfx, Action begin, Action commit,
            Action changed)
        {
            int cur = (int)p.Get();
            string preview = cur >= 0 && cur < AnchorNames.Length ? AnchorNames[cur] : "";
            if (ImGui.BeginCombo(p.Label + sfx, preview))
            {
                for (int n = 0; n < AnchorNames.Length; n++)
                {
                    if (!ImGui.Selectable(AnchorNames[n], n == cur) || n == cur) continue;
                    begin?.Invoke();
                    p.Set(n);
                    changed?.Invoke();
                    commit?.Invoke();
                }
                ImGui.EndCombo();
            }
            Tooltip(p);
        }

        private static void DrawTier(HudProp p, string key, Action begin, Action commit,
            Action changed)
        {
            // Three flag bits shown as three boxes on one line — quicker to read and set than a
            // three-item multi-select combo, and the B/S/R shorthand matches the tier labels
            // everywhere else in the HUD.
            int mask = (int)p.Get();
            bool bare = (mask & (int)HudTierMask.Bare) != 0;
            bool suited = (mask & (int)HudTierMask.Suited) != 0;
            bool robot = (mask & (int)HudTierMask.Robot) != 0;

            if (ImGui.Checkbox("B##hp_" + key + "_B", ref bare)) SetTier(p, mask, HudTierMask.Bare, bare, begin, commit, changed);
            ImGui.SameLine();
            if (ImGui.Checkbox("S##hp_" + key + "_S", ref suited)) SetTier(p, mask, HudTierMask.Suited, suited, begin, commit, changed);
            ImGui.SameLine();
            if (ImGui.Checkbox("R##hp_" + key + "_R", ref robot)) SetTier(p, mask, HudTierMask.Robot, robot, begin, commit, changed);
            ImGui.SameLine();
            ImGui.TextDisabled(p.Label);
            Tooltip(p);
        }

        private static void SetTier(HudProp p, int mask, HudTierMask bit, bool on, Action begin,
            Action commit, Action changed)
        {
            int next = on ? mask | (int)bit : mask & ~(int)bit;
            begin?.Invoke();
            p.Set(next);
            changed?.Invoke();
            commit?.Invoke();
        }

        // ------------------------------------------------------------------ colour reference

        private static void DrawColorRef(HudProp p, string key, Action begin, Action commit,
            Action cancel, Action noteChanged)
        {
            string cur = (string)p.Get() ?? "";
            Color fallback = p.ColorFallback != null ? p.ColorFallback() : Color.white;
            // The swatch always shows the RESOLVED colour, so a palette-named ref visibly tracks the
            // palette while a "#hex" literal shows its own value — one glance tells you which you have.
            Color resolved = HudPalette.Resolve(cur, fallback);
            ImGui.ColorButton("##sw_" + key, ToV4(resolved), SwatchFlags, new Vector2(18f, 18f));
            ImGui.SameLine();

            bool isPalette = HudPalette.IsPaletteName(cur);
            string preview = isPalette ? cur : "Custom colour";
            if (ImGui.BeginCombo(p.Label + "##hp_" + key, preview))
            {
                // Picking "Custom" hands the ref off to the wheel below by writing the current colour
                // as a literal; picking a palette name stores the NAME so it keeps tracking the palette.
                if (ImGui.Selectable("Custom colour", !isPalette))
                {
                    if (isPalette)
                    {
                        begin?.Invoke();
                        p.Set(HudPalette.ToHexRef(resolved));
                        noteChanged?.Invoke();
                        commit?.Invoke();
                    }
                }
                for (int n = 0; n < HudPalette.All.Count; n++)
                {
                    string name = HudPalette.All[n].Name;
                    if (ImGui.Selectable(name, string.Equals(name, cur, StringComparison.Ordinal)))
                    {
                        if (!string.Equals(name, cur, StringComparison.Ordinal))
                        {
                            begin?.Invoke();
                            p.Set(name);
                            noteChanged?.Invoke();
                            commit?.Invoke();
                        }
                    }
                }
                ImGui.EndCombo();
            }
            Tooltip(p);

            // Re-read: a "Custom" pick this frame flips the ref to a literal, and we want its wheel to
            // appear immediately rather than a frame late.
            cur = (string)p.Get() ?? "";
            if (!HudPalette.IsPaletteName(cur))
            {
                Color c = HudPalette.Resolve(cur, fallback);
                Vector4 v = ToV4(c);
                bool didChange = ImGui.ColorEdit4("##cw_" + key, ref v, WheelFlags);
                BeginContinuous(begin);
                if (didChange)
                {
                    p.Set(HudPalette.ToHexRef(new Color(v.x, v.y, v.z, v.w)));
                    noteChanged?.Invoke();
                }
                EndContinuous(commit, cancel);
            }
        }

        // ------------------------------------------------------------------ points

        private static void DrawPoints(HudProp p)
        {
            // Point lists are shaped on the canvas with the Draw tool, not typed here — there is no
            // sane textual widget for a polyline, so this row is purely a signpost.
            ImGui.TextDisabled(p.Label);
            ImGui.SameLine();
            ImGui.TextDisabled("(edit points with the Draw tool)");
        }

        // ------------------------------------------------------------------ shared helpers

        /// <summary>Undo bracket for the continuous widgets (sliders, drags, text, the colour wheel):
        /// begin on the frame the widget is grabbed, commit on the frame it is released after a real
        /// edit — so the whole drag is a single undo step.</summary>
        private static void BeginContinuous(Action begin)
        {
            if (ImGui.IsItemActivated()) begin?.Invoke();
        }

        private static void EndContinuous(Action commit, Action cancel)
        {
            if (ImGui.IsItemDeactivatedAfterEdit()) commit?.Invoke();
            else if (ImGui.IsItemDeactivated()) cancel?.Invoke();
        }

        private static void Tooltip(HudProp p)
        {
            if (!string.IsNullOrEmpty(p.Help) && ImGui.IsItemHovered()) ImGui.SetTooltip(p.Help);
        }

        private static Vector4 ToV4(Color c) { return new Vector4(c.r, c.g, c.b, c.a); }
    }
}
